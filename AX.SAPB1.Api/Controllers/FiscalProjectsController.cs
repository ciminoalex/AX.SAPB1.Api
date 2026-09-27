using AX.SAPB1.Api.Models;
using AX.SAPB1.Api.Services.FiscalProjects;
using Microsoft.AspNetCore.Mvc;

namespace AX.SAPB1.Api.Controllers
{
    /// <summary>
    /// Creazione dei progetti contabili (OPRJ) dal portale AX.360. La lettura resta in
    /// <c>GET /api/gl/fiscal-projects</c>. Risposte: 201 creato; 200 con <c>created = false</c> se il codice
    /// esplicito esisteva già; 400 payload non valido; 409 progressivo esaurito o collisioni ripetute;
    /// 502 rifiuto di SAP.
    /// </summary>
    [ApiController]
    [Route("api/fiscal-projects")]
    public class FiscalProjectsController : ControllerBase
    {
        private readonly IFiscalProjectService _service;
        private readonly ILogger<FiscalProjectsController> _logger;

        public FiscalProjectsController(IFiscalProjectService service, ILogger<FiscalProjectsController> logger)
        {
            _service = service;
            _logger = logger;
        }

        [HttpPost]
        public async Task<ActionResult<FiscalProjectCreateResult>> Create([FromBody] FiscalProjectCreateRequest request)
        {
            try
            {
                var outcome = await _service.CreateAsync(request);
                return StatusCode(outcome.StatusCode, outcome.Result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Errore nella creazione del progetto contabile «{Name}»", request?.Name);
                return StatusCode(500, new FiscalProjectCreateResult { Created = false, ErrorMessage = ex.Message });
            }
        }
    }
}
