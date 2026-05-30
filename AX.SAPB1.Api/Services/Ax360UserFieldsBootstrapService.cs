namespace AX.SAPB1.Api.Services
{
    /// <summary>
    /// All'avvio dell'applicazione garantisce (best-effort) l'esistenza dei campi utente AX.360
    /// sui documenti di marketing SAP B1. Gira in background per non bloccare lo startup se il
    /// Service Layer non è raggiungibile.
    /// </summary>
    public sealed class Ax360UserFieldsBootstrapService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<Ax360UserFieldsBootstrapService> _logger;

        public Ax360UserFieldsBootstrapService(
            IServiceScopeFactory scopeFactory,
            ILogger<Ax360UserFieldsBootstrapService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
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
