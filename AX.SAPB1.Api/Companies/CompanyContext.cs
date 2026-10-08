using System.Security.Claims;

namespace AX.SAPB1.Api.Companies
{
    /// <summary>Claim che porta la company scelta al login (chiave API o login utente).</summary>
    public static class CompanyClaims
    {
        public const string Company = "company";
    }

    /// <summary>
    /// La richiesta non ha una company valida. È un errore di sicurezza, non di dato: chi la riceve NON deve ripiegare
    /// sulla company principale, altrimenti un client di un'azienda leggerebbe lo schema di un'altra.
    /// </summary>
    public sealed class CompanyNotResolvedException : Exception
    {
        public CompanyNotResolvedException(string message) : base(message) { }
    }

    /// <summary>La company della richiesta (o del lavoro in background) corrente.</summary>
    public interface ICompanyContext
    {
        /// <summary>La company corrente. Lancia <see cref="CompanyNotResolvedException"/> se non è determinabile.</summary>
        CompanyProfile Current { get; }

        /// <summary>
        /// Fissa la company per il lavoro senza richiesta HTTP (servizi in background), finché non si fa Dispose.
        /// </summary>
        IDisposable Use(CompanyProfile company);
    }

    public sealed class CompanyContext : ICompanyContext
    {
        private readonly ICompanyRegistry _registry;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private CompanyProfile? _override;

        public CompanyContext(ICompanyRegistry registry, IHttpContextAccessor httpContextAccessor)
        {
            _registry = registry;
            _httpContextAccessor = httpContextAccessor;
        }

        public CompanyProfile Current => _override ?? Resolve(_httpContextAccessor.HttpContext?.User, _registry);

        public IDisposable Use(CompanyProfile company)
        {
            var previous = _override;
            _override = company;
            return new Restore(() => _override = previous);
        }

        /// <summary>
        /// La company dell'utente autenticato. Senza claim, o con una company non più configurata, si rifiuta: niente
        /// ripiego sulla principale.
        /// </summary>
        public static CompanyProfile Resolve(ClaimsPrincipal? user, ICompanyRegistry registry)
        {
            var id = user?.FindFirst(CompanyClaims.Company)?.Value;
            if (string.IsNullOrWhiteSpace(id))
                throw new CompanyNotResolvedException("La richiesta non indica la company: rifare il login (il token non porta il claim 'company').");
            if (!registry.TryGet(id, out var company))
                throw new CompanyNotResolvedException($"La company '{id}' non è configurata su questo servizio.");
            return company;
        }

        /// <summary>True se un utente autenticato porta una company valida. Gli anonimi passano (login, test).</summary>
        public static bool IsAllowed(ClaimsPrincipal user, ICompanyRegistry registry, out string? reason)
        {
            reason = null;
            if (user.Identity?.IsAuthenticated != true) return true;
            try
            {
                Resolve(user, registry);
                return true;
            }
            catch (CompanyNotResolvedException ex)
            {
                reason = ex.Message;
                return false;
            }
        }

        private sealed class Restore : IDisposable
        {
            private Action? _action;
            public Restore(Action action) => _action = action;
            public void Dispose() { _action?.Invoke(); _action = null; }
        }
    }
}
