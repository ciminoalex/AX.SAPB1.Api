using System.Diagnostics;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;

namespace AX.SAPB1.Api.Support
{
    public class LoggingHttpMessageHandler : DelegatingHandler
    {
        private readonly ILogger<LoggingHttpMessageHandler> _logger;

        public LoggingHttpMessageHandler(ILogger<LoggingHttpMessageHandler> logger)
        {
            _logger = logger;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();

            var requestInfo = new
            {
                Method = request.Method.Method,
                Url = request.RequestUri?.ToString(),
                Version = request.Version.ToString(),
                Headers = GetSafeHeaders(request.Headers),
                ContentHeaders = request.Content != null ? GetSafeHeaders(request.Content.Headers) : new Dictionary<string, string>(),
                ContentSizeBytes = await GetContentSizeAsync(request.Content)
            };

            using (_logger.BeginScope(new Dictionary<string, object?>
            {
                ["http.method"] = requestInfo.Method,
                ["http.url"] = requestInfo.Url,
            }))
            {
                _logger.LogInformation("Outbound HTTP start {Method} {Url} size={ContentSizeBytes}B", requestInfo.Method, requestInfo.Url, requestInfo.ContentSizeBytes);

                try
                {
                    var response = await base.SendAsync(request, cancellationToken);
                    stopwatch.Stop();

                    var responseSize = response.Content != null ? await GetContentSizeAsync(response.Content) : 0;
                    _logger.LogInformation(
                        "Outbound HTTP done {Method} {Url} -> {StatusCode} in {ElapsedMs} ms respSize={ResponseSizeBytes}B",
                        requestInfo.Method,
                        requestInfo.Url,
                        (int)response.StatusCode,
                        stopwatch.ElapsedMilliseconds,
                        responseSize);

                    return response;
                }
                catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    stopwatch.Stop();
                    _logger.LogError(ex, "Outbound HTTP timeout {Method} {Url} after {ElapsedMs} ms", requestInfo.Method, requestInfo.Url, stopwatch.ElapsedMilliseconds);
                    throw;
                }
                catch (Exception ex)
                {
                    stopwatch.Stop();
                    _logger.LogError(ex, "Outbound HTTP error {Method} {Url} after {ElapsedMs} ms", requestInfo.Method, requestInfo.Url, stopwatch.ElapsedMilliseconds);
                    throw;
                }
            }
        }

        private static async Task<long> GetContentSizeAsync(HttpContent? content)
        {
            if (content == null)
            {
                return 0;
            }

            if (content.Headers.ContentLength.HasValue)
            {
                return content.Headers.ContentLength.Value;
            }

            try
            {
                // As a fallback, buffer to compute size (may allocate). Limit to small payloads.
                var bytes = await content.ReadAsByteArrayAsync();
                return bytes.LongLength;
            }
            catch
            {
                return 0;
            }
        }

        private static Dictionary<string, string> GetSafeHeaders(HttpHeaders headers)
        {
            var safe = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in headers)
            {
                // Avoid logging sensitive values like Authorization or cookies
                if (string.Equals(header.Key, "Authorization", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(header.Key, "Cookie", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(header.Key, "Set-Cookie", StringComparison.OrdinalIgnoreCase))
                {
                    safe[header.Key] = "<redacted>";
                }
                else
                {
                    safe[header.Key] = string.Join(",", header.Value);
                }
            }
            return safe;
        }
    }
}


