using AX.SAPB1.Api.Models;
using AX.SAPB1.Api.Services.SalesDocuments;
using Microsoft.AspNetCore.Mvc;

namespace AX.SAPB1.Api.Controllers
{
    /// <summary>
    /// Documenti di vendita del portale AX.360: fattura oppure ordine cliente, in bozza oppure definitivo.
    /// Idempotente sul correlationId (<c>U_AX360_InvId</c>): 201 se creato, 200 se esisteva già (con i dati del
    /// documento reale), 400 se il payload non è valido, 403 se le fatture definitive sono disattivate
    /// sull'istanza, 502 se SAP rifiuta, 500 su errore interno o campo di correlazione assente.
    /// Il vecchio <c>POST /api/invoices</c> (bozza fattura) resta invariato: è quello della produzione attuale.
    /// </summary>
    [ApiController]
    [Route("api/sales-documents")]
    public class SalesDocumentsController : ControllerBase
    {
        private readonly ISalesDocumentService _service;
        private readonly ILogger<SalesDocumentsController> _logger;

        public SalesDocumentsController(ISalesDocumentService service, ILogger<SalesDocumentsController> logger)
        {
            _service = service;
            _logger = logger;
        }

        [HttpPost]
        public async Task<ActionResult<SalesDocumentResult>> Create([FromBody] SalesDocumentRequest request)
        {
            try
            {
                var outcome = await _service.CreateAsync(request);
                return StatusCode(outcome.StatusCode, outcome.Result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Errore nel push del documento di vendita {CorrelationId}", request?.CorrelationId);
                return StatusCode(500, new SalesDocumentResult { Success = false, ErrorMessage = ex.Message });
            }
        }

        /// <summary>
        /// Anteprima per il collaudo: esegue le stesse verifiche del POST (validazione, campi utente, documento
        /// esistente, unità di misura) e restituisce il payload che verrebbe inviato al Service Layer, senza
        /// scrivere nulla in SAP.
        /// </summary>
        [HttpPost("preview")]
        public async Task<ActionResult<SalesDocumentPreview>> Preview([FromBody] SalesDocumentRequest request)
        {
            try
            {
                var preview = await _service.PreviewAsync(request);
                return preview.Errors.Count > 0 ? BadRequest(preview) : Ok(preview);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Errore nell'anteprima del documento di vendita {CorrelationId}", request?.CorrelationId);
                return StatusCode(500, "Errore interno durante l'anteprima del documento di vendita");
            }
        }
    }
}
