using System.Security.Cryptography;
using System.Text;

namespace AX.SAPB1.Api.Companies
{
    /// <summary>Le company servite da questa istanza, costruite una volta all'avvio dalla configurazione.</summary>
    public interface ICompanyRegistry
    {
        IReadOnlyList<CompanyProfile> Companies { get; }

        CompanyProfile Primary { get; }

        bool TryGet(string? id, out CompanyProfile company);

        /// <summary>La company a cui appartiene la chiave API presentata, oppure null. Confronto a tempo costante.</summary>
        CompanyProfile? FindByApiKey(string presented);
    }

    /// <summary>
    /// Registro delle company. Configurazione:
    /// <code>
    /// "SapB1": { "CompanyDB": "SBO_MTF", "CompanyId": "default", ... }   // company principale (storica)
    /// "Auth":  { "ApiKeys": [ "..." ] }                                 // chiavi della principale
    /// "Companies": {
    ///   "htdi": { "CompanyDB": "SBOHITECH_PROD", "ApiKeys": [ "..." ], "ConnectionString": "(opzionale)" }
    /// }
    /// </code>
    /// <para>Ogni errore di configurazione fa fallire l'avvio: meglio un servizio fermo che uno che risponde con lo
    /// schema sbagliato.</para>
    /// </summary>
    public sealed class CompanyRegistry : ICompanyRegistry
    {
        public const string DefaultPrimaryId = "default";

        private readonly Dictionary<string, CompanyProfile> _byId;
        private readonly List<(byte[] Key, CompanyProfile Company)> _keys;

        public CompanyRegistry(IConfiguration configuration)
        {
            var sapB1 = configuration.GetSection("SapB1");

            var primaryDb = sapB1["CompanyDB"];
            if (string.IsNullOrWhiteSpace(primaryDb))
                throw new InvalidOperationException("Configurazione mancante: SapB1:CompanyDB (company principale).");

            var defaultConnection = configuration.GetConnectionString("DefaultDatabase");
            if (string.IsNullOrWhiteSpace(defaultConnection))
                throw new InvalidOperationException("Configurazione mancante: ConnectionStrings:DefaultDatabase.");

            var primaryId = NormalizeId(sapB1["CompanyId"]) ?? DefaultPrimaryId;
            var primary = new CompanyProfile(
                primaryId,
                primaryDb.Trim(),
                defaultConnection,
                ReadKeys(configuration.GetSection("Auth"), "ApiKey", "ApiKeys"),
                isPrimary: true,
                settings: sapB1,
                inheritFrom: null);

            var companies = new List<CompanyProfile> { primary };
            foreach (var section in configuration.GetSection("Companies").GetChildren())
            {
                var id = NormalizeId(section.Key)
                    ?? throw new InvalidOperationException("Companies: identificativo di company vuoto.");
                if (companies.Any(c => c.Id == id))
                    throw new InvalidOperationException($"Companies:{section.Key}: identificativo già usato da un'altra company.");

                var db = section["CompanyDB"];
                if (string.IsNullOrWhiteSpace(db))
                    throw new InvalidOperationException($"Configurazione mancante: Companies:{section.Key}:CompanyDB.");
                if (companies.Any(c => string.Equals(c.CompanyDB, db.Trim(), StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException($"Companies:{section.Key}: lo schema {db} è già servito da un'altra company.");

                var connection = string.IsNullOrWhiteSpace(section["ConnectionString"]) ? defaultConnection : section["ConnectionString"]!;
                companies.Add(new CompanyProfile(
                    id, db.Trim(), connection, ReadKeys(section, "ApiKey", "ApiKeys"),
                    isPrimary: false, settings: section, inheritFrom: sapB1));
            }

            _keys = new List<(byte[], CompanyProfile)>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var company in companies)
            {
                foreach (var key in company.ApiKeys)
                {
                    // Una chiave che aprisse due company renderebbe la company una scelta del caso.
                    if (!seen.Add(key))
                        throw new InvalidOperationException($"La stessa chiave API è configurata per più company (anche {company.Id}).");
                    _keys.Add((Encoding.UTF8.GetBytes(key), company));
                }
            }

            Companies = companies;
            Primary = primary;
            _byId = companies.ToDictionary(c => c.Id, StringComparer.Ordinal);
        }

        public IReadOnlyList<CompanyProfile> Companies { get; }

        public CompanyProfile Primary { get; }

        public bool TryGet(string? id, out CompanyProfile company)
        {
            company = null!;
            var normalized = NormalizeId(id);
            return normalized != null && _byId.TryGetValue(normalized, out company!);
        }

        public CompanyProfile? FindByApiKey(string presented)
        {
            if (string.IsNullOrWhiteSpace(presented)) return null;
            var bytes = Encoding.UTF8.GetBytes(presented.Trim());
            CompanyProfile? match = null;
            // Si confrontano tutte le chiavi, senza uscire alla prima: il tempo non dice quale company esiste.
            foreach (var (key, company) in _keys)
            {
                if (CryptographicOperations.FixedTimeEquals(key, bytes)) match = company;
            }
            return match;
        }

        internal static string? NormalizeId(string? id) =>
            string.IsNullOrWhiteSpace(id) ? null : id.Trim().ToLowerInvariant();

        private static IReadOnlyList<string> ReadKeys(IConfiguration section, string singleKey, string arrayKey)
        {
            var keys = new List<string>();
            var single = section[singleKey];
            if (!string.IsNullOrWhiteSpace(single)) keys.Add(single.Trim());
            foreach (var child in section.GetSection(arrayKey).GetChildren())
            {
                if (!string.IsNullOrWhiteSpace(child.Value)) keys.Add(child.Value.Trim());
            }
            return keys.Distinct(StringComparer.Ordinal).ToList();
        }
    }
}
