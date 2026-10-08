namespace AX.SAPB1.Api.Companies
{
    /// <summary>
    /// Una company SAP Business One servita da questa istanza del servizio: lo schema HANA/CompanyDB, le chiavi API
    /// che la identificano e le sue impostazioni.
    /// <para>
    /// <b>La company principale</b> è la configurazione storica (<c>SapB1:*</c>, <c>Auth:ApiKey(s)</c>,
    /// <c>ConnectionStrings:DefaultDatabase</c>): un server già installato continua a funzionare identico senza toccare
    /// appsettings.json — conta perché deploy.ps1 sostituisce solo l'eseguibile.
    /// </para>
    /// <para>
    /// <b>Le company aggiuntive</b> (<c>Companies:&lt;id&gt;</c>) leggono le impostazioni dalla propria sezione. Dalla
    /// principale ereditano SOLO le chiavi di <see cref="InheritableKeys"/> (credenziali del Service Layer e tempi):
    /// mai un interruttore di scrittura o un dato della company (articolo T&amp;M, gruppo IVA, schema dei codici). Una
    /// chiave dimenticata deve lasciare la funzione spenta, non accenderla con il valore di un'altra azienda.
    /// </para>
    /// </summary>
    public sealed class CompanyProfile
    {
        /// <summary>Chiavi che una company aggiuntiva eredita dalla sezione <c>SapB1</c> se non le dichiara.</summary>
        public static readonly IReadOnlySet<string> InheritableKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "UserName",
            "Password",
            "SessionTimeoutMinutes",
            "Write:CommandTimeoutSeconds",
            "Write:MaxBatchSize",
        };

        private readonly IConfiguration _settings;
        private readonly IConfiguration? _inheritFrom;

        internal CompanyProfile(
            string id,
            string companyDb,
            string connectionString,
            IReadOnlyList<string> apiKeys,
            bool isPrimary,
            IConfiguration settings,
            IConfiguration? inheritFrom)
        {
            Id = id;
            CompanyDB = companyDb;
            ConnectionString = connectionString;
            ApiKeys = apiKeys;
            IsPrimary = isPrimary;
            _settings = settings;
            _inheritFrom = inheritFrom;
        }

        /// <summary>Identificativo della company nel servizio (claim <c>company</c>), es. "default", "htdi".</summary>
        public string Id { get; }

        /// <summary>Schema HANA / CompanyDB del Service Layer, es. "SBO_MTF".</summary>
        public string CompanyDB { get; }

        /// <summary>Stringa ODBC verso HANA. L'utenza deve poter leggere <see cref="CompanyDB"/>.</summary>
        public string ConnectionString { get; }

        /// <summary>Chiavi API (header X-Api-Key) che entrano in questa company.</summary>
        public IReadOnlyList<string> ApiKeys { get; }

        /// <summary>True per la company della configurazione storica <c>SapB1:*</c>.</summary>
        public bool IsPrimary { get; }

        /// <summary>
        /// Impostazione della company con chiave relativa (es. "Write:Enabled", "TimeAndMaterialsItemCode"). Per la
        /// principale è <c>SapB1:&lt;chiave&gt;</c>; per un'aggiuntiva <c>Companies:&lt;id&gt;:&lt;chiave&gt;</c>, con
        /// eredità dalla principale solo per <see cref="InheritableKeys"/>.
        /// </summary>
        public string? Get(string key)
        {
            var own = _settings[key];
            if (own != null) return own;
            return _inheritFrom != null && InheritableKeys.Contains(key) ? _inheritFrom[key] : null;
        }

        /// <summary>
        /// Company in sola lettura: il servizio rifiuta ogni chiamata che scrive in SAP (vedi CompanyGate). Le company
        /// aggiuntive lo sono per default e si aprono solo con <c>Companies:&lt;id&gt;:AllowWrites: true</c>; la
        /// principale resta scrivibile come prima, salvo <c>SapB1:ReadOnly: true</c>.
        /// </summary>
        public bool ReadOnly => IsPrimary ? IsEnabled("ReadOnly") : !IsEnabled("AllowWrites");

        /// <summary>Interruttore booleano: assente o non leggibile come booleano ⇒ false.</summary>
        public bool IsEnabled(string key) => bool.TryParse(Get(key), out var v) && v;

        public override string ToString() => $"{Id} ({CompanyDB})";
    }
}
