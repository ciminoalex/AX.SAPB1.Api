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
    /// </summary>
    public sealed class Ax360UserFieldsBootstrapService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<Ax360UserFieldsBootstrapService> _logger;

        public Ax360UserFieldsBootstrapService(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            ILogger<Ax360UserFieldsBootstrapService> logger)
        {
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!(bool.TryParse(_configuration["SapB1:Bootstrap:UserFields:Enabled"], out var enabled) && enabled))
            {
                _logger.LogInformation("Provisioning UDF AX.360 all'avvio disattivato (SapB1:Bootstrap:UserFields:Enabled non è true): metadati SAP non toccati.");
                return;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var sl = scope.ServiceProvider.GetRequiredService<ISapB1ServiceLayerService>();
                await sl.EnsureAx360UserFieldsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Provisioning UDF AX.360 all'avvio non completato (verrà ritentato al prossimo riavvio).");
            }
        }
    }
}
