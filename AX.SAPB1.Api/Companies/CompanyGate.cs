namespace AX.SAPB1.Api.Companies
{
    /// <summary>
    /// Cancello davanti a ogni endpoint autenticato, subito dopo l'autorizzazione:
    /// <list type="number">
    /// <item>l'utente deve portare una company configurata (claim del login), altrimenti 403 — mai un ripiego sulla
    /// principale;</item>
    /// <item>su una company in sola lettura (<see cref="CompanyProfile.ReadOnly"/>) passano solo le letture: GET e i
    /// pochi POST che leggono (<see cref="ReadOnlyPosts"/>).</item>
    /// </list>
    /// Gli endpoint anonimi (login, test) non hanno utente e passano.
    /// </summary>
    public static class CompanyGate
    {
        /// <summary>POST che leggono soltanto (il corpo porta un elenco di chiavi, troppo lungo per una query string).</summary>
        internal static readonly IReadOnlySet<string> ReadOnlyPosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "/api/gl/lines/by-entry-ids",
        };

        public readonly record struct Verdict(bool Allowed, string? Reason, CompanyProfile? Company);

        public static Verdict Check(HttpContext context, ICompanyRegistry registry)
        {
            if (context.User.Identity?.IsAuthenticated != true) return new Verdict(true, null, null);

            CompanyProfile company;
            try
            {
                company = CompanyContext.Resolve(context.User, registry);
            }
            catch (CompanyNotResolvedException ex)
            {
                return new Verdict(false, ex.Message, null);
            }

            if (company.ReadOnly && !IsRead(context.Request.Method, context.Request.Path))
                return new Verdict(false, $"La company '{company.Id}' è in sola lettura su questo servizio.", company);

            return new Verdict(true, null, company);
        }

        internal static bool IsRead(string method, PathString path) =>
            HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method)
            || (HttpMethods.IsPost(method) && path.HasValue && ReadOnlyPosts.Contains(path.Value!.TrimEnd('/')));
    }
}
