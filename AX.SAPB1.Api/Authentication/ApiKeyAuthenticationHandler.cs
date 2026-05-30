using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace AX.SAPB1.Api.Authentication
{
    /// <summary>
    /// Autenticazione machine-to-machine a chiave API statica (header <c>X-Api-Key</c>),
    /// usata dal portale AX.360 per consumare gli endpoint ERP. Affianca il JWT Bearer
    /// (vedi policy combinata in Program.cs): un endpoint è accessibile con JWT valido
    /// OPPURE con una chiave API valida.
    ///
    /// Le chiavi accettate sono configurate in <c>Auth:ApiKeys</c> (array) o <c>Auth:ApiKey</c> (singola).
    /// </summary>
    public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "ApiKey";
        public const string HeaderName = "X-Api-Key";

        private readonly IConfiguration _configuration;

        public ApiKeyAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            IConfiguration configuration)
            : base(options, logger, encoder)
        {
            _configuration = configuration;
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            // Header assente → nessun esito: lascia provare gli altri schemi (es. JWT).
            if (!Request.Headers.TryGetValue(HeaderName, out var provided) || string.IsNullOrWhiteSpace(provided))
                return Task.FromResult(AuthenticateResult.NoResult());

            var presented = provided.ToString().Trim();
            var allowed = GetConfiguredKeys();

            if (allowed.Count == 0)
            {
                Logger.LogWarning("X-Api-Key presentato ma nessuna chiave configurata in Auth:ApiKeys/Auth:ApiKey.");
                return Task.FromResult(AuthenticateResult.Fail("Nessuna chiave API configurata."));
            }

            // Confronto a tempo costante per evitare timing attack.
            var match = allowed.Any(k => CryptographicEquals(k, presented));
            if (!match)
                return Task.FromResult(AuthenticateResult.Fail("Chiave API non valida."));

            var claims = new[]
            {
                new Claim(ClaimTypes.Name, "ax360-erp"),
                new Claim("client_type", "api_key"),
            };
            var identity = new ClaimsIdentity(claims, SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        private List<string> GetConfiguredKeys()
        {
            var keys = new List<string>();

            var single = _configuration["Auth:ApiKey"];
            if (!string.IsNullOrWhiteSpace(single)) keys.Add(single.Trim());

            var multi = _configuration.GetSection("Auth:ApiKeys").Get<string[]>();
            if (multi != null)
                keys.AddRange(multi.Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()));

            return keys.Distinct().ToList();
        }

        private static bool CryptographicEquals(string a, string b)
        {
            var ba = System.Text.Encoding.UTF8.GetBytes(a);
            var bb = System.Text.Encoding.UTF8.GetBytes(b);
            return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(ba, bb);
        }
    }
}
