using AX.SAPB1.Api.Companies;

namespace AX.SAPB1.Api.Services
{
    /// <summary>
    /// All'avvio dell'applicazione garantisce (best-effort) l'esistenza dei campi utente AX.360
    /// sui documenti di marketing SAP B1. Gira in background per non bloccare lo startup se il
    /// Service Layer non è raggiungibile.
    /// <para>
    /// <b>Opt-in esplicito</b> (<c>SapB1:Bootstrap:UserFields:Enabled</c>, chiave assente ⇒ spento): creare
    /// un campo utente modifica i metadati della company SAP. Con la correlazione estesa agli ordini (ORDR),
    /// una nuova build copiata sull'istanza di produzione — deploy.ps1 copia solo l'exe, non
    /// appsettings.json — altererebbe la company di produzione per pura assenza di configurazione. Chi vuole
    /// il provisioning (es. l'istanza di test su una company copiata) lo dichiara.
    /// </para>
    /// <para>
    /// <b>Per company.</b> L'interruttore si legge company per company (<c>SapB1:Bootstrap:UserFields:Enabled</c> per la
    /// principale, <c>Companies:&lt;id&gt;:Bootstrap:UserFields:Enabled</c> per le aggiuntive, mai ereditato), e una
    /// company in sola lettura non viene mai toccata, anche con l'interruttore acceso.
    /// </para>
    /// </summary>
    public sealed class Ax360UserFieldsBootstrapService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ICompanyRegistry _companies;
        private readonly ILogger<Ax360UserFieldsBootstrapService> _logger;

        public Ax360UserFieldsBootstrapService(
            IServiceScopeFactory scopeFactory,
            ICompanyRegistry companies,
            ILogger<Ax360UserFieldsBootstrapService> logger)
        {
            _scopeFactory = scopeFactory;
            _companies = companies;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            foreach (var company in _companies.Companies)
            {
                if (!company.IsEnabled("Bootstrap:UserFields:Enabled"))
                {
                    _logger.LogInformation("Provisioning UDF AX.360 all'avvio disattivato per la company {Company}: metadati SAP non toccati.", company);
                    continue;
                }
                if (company.ReadOnly)
                {
                    _logger.LogWarning("Provisioning UDF AX.360 chiesto per la company {Company}, che è in sola lettura: ignorato.", company);
                    continue;
                }

                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    using var _ = scope.ServiceProvider.GetRequiredService<ICompanyContext>().Use(company);
                    var sl = scope.ServiceProvider.GetRequiredService<ISapB1ServiceLayerService>();
                    await sl.EnsureAx360UserFieldsAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Provisioning UDF AX.360 all'avvio non completato per la company {Company} (verrà ritentato al prossimo riavvio).", company);
                }
            }
        }
    }
}
