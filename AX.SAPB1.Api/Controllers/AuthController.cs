using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using AX.SAPB1.Api.Companies;
using AX.SAPB1.Api.Services;

namespace AX.SAPB1.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly ISapB1AuthService _sapAuth;
        private readonly ICredentialStore _credentialStore;
        private readonly IConfiguration _configuration;
        private readonly ICompanyRegistry _companies;
        private readonly ILogger<AuthController> _logger;

        public AuthController(ISapB1AuthService sapAuth, ICredentialStore credentialStore, IConfiguration configuration, ICompanyRegistry companies, ILogger<AuthController> logger)
        {
            _sapAuth = sapAuth;
            _credentialStore = credentialStore;
            _configuration = configuration;
            _companies = companies;
            _logger = logger;
        }

        /// <summary>
        /// Login utente. <paramref name="Company"/> sceglie la company (es. "htdi") e quindi lo schema SAP su cui
        /// lavorerà il token: assente ⇒ la company principale, come prima del multi-company.
        /// </summary>
        public record LoginRequest(string UserName, string Password, string? Company = null);

        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest("UserName e Password sono obbligatori");
            }

            CompanyProfile company;
            if (string.IsNullOrWhiteSpace(request.Company))
            {
                company = _companies.Primary;
            }
            else if (!_companies.TryGet(request.Company, out company))
            {
                return BadRequest($"Company '{request.Company}' non configurata su questo servizio");
            }

            var valid = await _sapAuth.ValidateCredentialsAsync(request.UserName, request.Password, company.CompanyDB);
            if (!valid)
            {
                return Unauthorized("Credenziali non valide per SAP Business One");
            }

            var jwtSection = _configuration.GetSection("Jwt");
            var key = jwtSection["Key"] ?? throw new InvalidOperationException("Jwt:Key non configurato");
            var issuer = jwtSection["Issuer"];
            var audience = jwtSection["Audience"];
            var expiresMinutes = int.TryParse(jwtSection["ExpiresMinutes"], out var m) ? m : 60;

            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, request.UserName),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(CompanyClaims.Company, company.Id),
            };

            var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256);
            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(expiresMinutes),
                signingCredentials: creds
            );

            var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

            // Store credentials bound to this token (keyed by JTI)
            var jti = token.Id;
            _credentialStore.SaveCredentials(jti, request.UserName, request.Password);

            _logger.LogInformation("Login SAP B1 riuscito per {UserName} sulla company {Company}", request.UserName, company);
            return Ok(new { token = tokenString, expiresIn = expiresMinutes * 60, company = company.Id });
        }
    }
}


