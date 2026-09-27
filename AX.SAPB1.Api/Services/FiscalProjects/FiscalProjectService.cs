using System.Globalization;
using AX.SAPB1.Api.Models;
using AX.SAPB1.Api.Services.SalesDocuments;

namespace AX.SAPB1.Api.Services.FiscalProjects
{
    public sealed record FiscalProjectOutcome(int StatusCode, FiscalProjectCreateResult Result);

    public interface IFiscalProjectService
    {
        Task<FiscalProjectOutcome> CreateAsync(FiscalProjectCreateRequest request);
    }

    /// <summary>
    /// Creazione dei progetti contabili (OPRJ) dal portale, con codice generato dal pattern
    /// <c>SapB1:FiscalProjectCodePattern</c> (default <see cref="FiscalProjectCodePattern.DefaultPattern"/>).
    ///
    /// <para><b>Concorrenza.</b> "Massimo + 1" non è atomico: due creazioni simultanee calcolerebbero lo stesso
    /// codice. Nel processo le creazioni si serializzano con un semaforo; fuori dal processo (un utente che crea
    /// un progetto dal client SAP nello stesso istante) SAP rifiuta il doppione con "già esistente" e si
    /// ricalcola, fino a <see cref="MaxAttempts"/> tentativi.</para>
    /// </summary>
    public sealed class FiscalProjectService : IFiscalProjectService
    {
        internal const int MaxAttempts = 3;

        private static readonly SemaphoreSlim CreationLock = new(1, 1);

        private readonly IDbOdbcService _db;
        private readonly ISapB1ServiceLayerService _sl;
        private readonly IConfiguration _configuration;
        private readonly ILogger<FiscalProjectService> _logger;

        public FiscalProjectService(IDbOdbcService db, ISapB1ServiceLayerService sl, IConfiguration configuration, ILogger<FiscalProjectService> logger)
        {
            _db = db;
            _sl = sl;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<FiscalProjectOutcome> CreateAsync(FiscalProjectCreateRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Name))
                return Fail(400, "Il nome del progetto contabile è obbligatorio.");
            if (request.ValidTo != null && request.ValidFrom != null && request.ValidTo.Value.Date < request.ValidFrom.Value.Date)
                return Fail(400, "validTo non può precedere validFrom.");

            var explicitCode = string.IsNullOrWhiteSpace(request.Code) ? null : request.Code.Trim();
            if (explicitCode != null && explicitCode.Length > FiscalProjectCodePattern.MaxCodeLength)
                return Fail(400, $"Il codice supera i {FiscalProjectCodePattern.MaxCodeLength} caratteri di OPRJ.PrjCode.");

            FiscalProjectCodePattern pattern;
            try
            {
                pattern = FiscalProjectCodePattern.Parse(_configuration["SapB1:FiscalProjectCodePattern"]);
            }
            catch (ArgumentException ex)
            {
                return Fail(500, $"SapB1:FiscalProjectCodePattern non valido: {ex.Message}");
            }

            var validFrom = (request.ValidFrom ?? DateTime.Today).Date;
            var year = validFrom.Year;
            var lengths = await _db.GetColumnLengthsAsync("OPRJ");
            var name = SalesDocumentPayloadBuilder.Truncate(request.Name.Trim(), SalesDocumentPayloadBuilder.LengthOf(lengths, "PrjName"))!;

            await CreationLock.WaitAsync();
            try
            {
                if (explicitCode != null)
                {
                    if (await _db.FiscalProjectExistsAsync(explicitCode))
                        return new FiscalProjectOutcome(200, new FiscalProjectCreateResult { Code = explicitCode, Created = false });

                    var response = await _sl.CreateFinancialProjectAsync(BuildPayload(explicitCode, name, validFrom, request.ValidTo));
                    if (response.IsSuccess)
                        return Created(explicitCode, name);
                    if (ServiceLayerErrors.IsAlreadyExists(response.Body))
                        return new FiscalProjectOutcome(200, new FiscalProjectCreateResult { Code = explicitCode, Created = false });
                    return Rejected(explicitCode, response);
                }

                for (var attempt = 1; attempt <= MaxAttempts; attempt++)
                {
                    var existing = await _db.GetFiscalProjectCodesLikeAsync(pattern.LikePattern(year));
                    string code;
                    try
                    {
                        code = pattern.Next(year, existing);
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Fail(409, ex.Message);
                    }

                    var response = await _sl.CreateFinancialProjectAsync(BuildPayload(code, name, validFrom, request.ValidTo));
                    if (response.IsSuccess)
                        return Created(code, name);

                    if (!ServiceLayerErrors.IsAlreadyExists(response.Body))
                        return Rejected(code, response);

                    _logger.LogWarning("Progetto contabile {Code} già esistente (tentativo {Attempt}/{Max}): ricalcolo del codice.", code, attempt, MaxAttempts);
                }

                return Fail(409, $"Impossibile assegnare un codice libero al progetto contabile dopo {MaxAttempts} tentativi: riprovare.");
            }
            finally
            {
                CreationLock.Release();
            }
        }

        /// <summary>Payload di <c>POST Projects</c>. Date come giorno; ValidTo omessa se non indicata.</summary>
        internal static Dictionary<string, object?> BuildPayload(string code, string name, DateTime validFrom, DateTime? validTo)
        {
            var payload = new Dictionary<string, object?>
            {
                ["Code"] = code,
                ["Name"] = name,
                ["Active"] = "tYES",
                ["ValidFrom"] = validFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            };
            if (validTo != null) payload["ValidTo"] = validTo.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return payload;
        }

        private FiscalProjectOutcome Created(string code, string name)
        {
            _logger.LogInformation("Progetto contabile creato: {Code} «{Name}».", code, name);
            return new FiscalProjectOutcome(201, new FiscalProjectCreateResult { Code = code, Created = true });
        }

        private FiscalProjectOutcome Rejected(string code, ServiceLayerResponse response)
        {
            _logger.LogError("Creazione progetto contabile {Code} rifiutata da SAP ({StatusCode}): {Body}", code, response.StatusCode, response.Body);
            return new FiscalProjectOutcome(502, new FiscalProjectCreateResult
            {
                Code = code,
                Created = false,
                ErrorMessage = $"SAP B1 ha rifiutato il progetto contabile ({response.StatusCode}): {ServiceLayerErrors.ExtractMessage(response.Body)}",
            });
        }

        private static FiscalProjectOutcome Fail(int statusCode, string message)
            => new(statusCode, new FiscalProjectCreateResult { Created = false, ErrorMessage = message });
    }
}
