using System.Text;
using Newtonsoft.Json;

namespace AX.SAPB1.Api.Services
{
    public class SapB1AuthService : ISapB1AuthService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<SapB1AuthService> _logger;

        public SapB1AuthService(HttpClient httpClient, IConfiguration configuration, ILogger<SapB1AuthService> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<bool> ValidateCredentialsAsync(string userName, string password, CancellationToken cancellationToken = default)
        {
            var loginData = new
            {
                CompanyDB = _configuration["SapB1:CompanyDB"],
                UserName = userName,
                Password = password
            };

            var json = JsonConvert.SerializeObject(loginData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            try
            {
                var response = await _httpClient.PostAsync("Login", content, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }

                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("SAP B1 credential validation failed: {Status} {Error}", response.StatusCode, error);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating SAP B1 credentials");
                return false;
            }
        }
    }
}


