using AX.SAPB1.Api.Models;
using AX.SAPB1.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AX.SAPB1.Api.Controllers
{
    /// <summary>
    /// Fatture A/R per il portale AX.360:
    /// - GET: lettura delle fatture definitive (OINV) per il mirror finanziario;
    /// - POST: push di una fattura AX come BOZZA SAP B1 (da confermare manualmente in SAP).
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class InvoicesController : ControllerBase
    {
        private readonly IDbOdbcService _dbOdbcService;
        private readonly ISapB1ServiceLayerService _slService;
        private readonly ILogger<InvoicesController> _logger;

        public InvoicesController(
            IDbOdbcService dbOdbcService,
            ISapB1ServiceLayerService slService,
            ILogger<InvoicesController> logger)
        {
            _dbOdbcService = dbOdbcService;
            _slService = slService;
            _logger = logger;
        }

        /// <summary>
        /// Fatture A/R definitive. Con <paramref name="since"/> il fetch è incrementale per data di
        /// ULTIMA MODIFICA (OINV.UpdateDate), non per data di emissione: così gli incassi registrati
        /// su fatture già emesse rientrano nel delta e lo stato di pagamento si propaga al portale.
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<ErpInvoiceDto>>> Get([FromQuery] DateTime? since)
        {
            try
            {
                var list = await _dbOdbcService.GetInvoicesAsync(since);
                return Ok(list);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving invoices (since={Since})", since);
                return StatusCode(500, "Errore interno del server durante il recupero delle fatture");
            }
        }

        /// <summary>
        /// Crea una bozza di fattura A/R in SAP B1 a partire dalla fattura AX.360.
        /// Idempotente sul codice interno AX (UDF di correlazione): se esiste già una bozza/fattura
        /// per lo stesso Ax360InvoiceId, restituisce l'esito esistente senza creare doppioni.
        /// </summary>
        [HttpPost]
        public async Task<ActionResult<ErpInvoicePushResult>> Post([FromBody] ErpInvoicePushDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.ErpCustomerCode))
                return BadRequest("Payload fattura non valido: ErpCustomerCode obbligatorio.");
            if (string.IsNullOrWhiteSpace(dto.Ax360InvoiceId))
                return BadRequest("Payload fattura non valido: Ax360InvoiceId obbligatorio per la correlazione.");

            try
            {
                // Anti-doppione: la bozza/fattura potrebbe già esistere (retry del push).
                var existing = await _dbOdbcService.FindDocumentByCorrelationIdAsync(dto.Ax360InvoiceId);
                if (existing != null)
                {
                    _logger.LogInformation("Push fattura {InvId}: documento già presente ({Status} {DocNum}), nessun doppione creato.",
                        dto.Ax360InvoiceId, existing.DocStatus, existing.ErpDocNumber);
                    return Ok(new ErpInvoicePushResult
                    {
                        Success = true,
                        ErpDocId = existing.ErpDocId,
                        ErpDocNumber = existing.ErpDocNumber,
                        DocStatus = existing.DocStatus,
                    });
                }

                var result = await _slService.CreateInvoiceDraftAsync(dto);
                if (!result.Success)
                    return StatusCode(502, result);

                return StatusCode(201, result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error pushing invoice {InvId} as draft", dto.Ax360InvoiceId);
                return StatusCode(500, new ErpInvoicePushResult { Success = false, ErrorMessage = ex.Message });
            }
        }
    }
}
