using System.Security.Claims;
using System.Text.Encodings.Web;
using AX.SAPB1.Api.Companies;
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
    /// <para><b>La chiave è il login del portale: decide la company.</b> Ogni chiave appartiene a una sola company
    /// (<c>Auth:ApiKeys</c> per la principale, <c>Companies:&lt;id&gt;:ApiKeys</c> per le aggiuntive, vedi
    /// <see cref="CompanyRegistry"/>) e finisce nel claim <see cref="CompanyClaims.Company"/>: ogni tenant del portale
    /// entra con la sua chiave e vede solo il suo schema.</para>
    /// </summary>
    public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "ApiKey";
        public const string HeaderName = "X-Api-Key";

        private readonly ICompanyRegistry _companies;

        public ApiKeyAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            ICompanyRegistry companies)
            : base(options, logger, encoder)
        {
            _companies = companies;
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            // Header assente → nessun esito: lascia provare gli altri schemi (es. JWT).
            if (!Request.Headers.TryGetValue(HeaderName, out var provided) || string.IsNullOrWhiteSpace(provided))
                return Task.FromResult(AuthenticateResult.NoResult());

            if (_companies.Companies.All(c => c.ApiKeys.Count == 0))
            {
                Logger.LogWarning("X-Api-Key presentato ma nessuna chiave configurata (Auth:ApiKeys / Companies:<id>:ApiKeys).");
                return Task.FromResult(AuthenticateResult.Fail("Nessuna chiave API configurata."));
            }

            // Confronto a tempo costante, su tutte le chiavi di tutte le company.
            var company = _companies.FindByApiKey(provided.ToString());
            if (company == null)
                return Task.FromResult(AuthenticateResult.Fail("Chiave API non valida."));

            var claims = new[]
            {
                new Claim(ClaimTypes.Name, "ax360-erp"),
                new Claim("client_type", "api_key"),
                new Claim(CompanyClaims.Company, company.Id),
            };
            var identity = new ClaimsIdentity(claims, SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
