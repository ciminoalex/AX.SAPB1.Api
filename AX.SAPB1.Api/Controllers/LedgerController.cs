using AX.SAPB1.Api.Models;
using AX.SAPB1.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AX.SAPB1.Api.Controllers
{
    /// <summary>
    /// Partitario clienti (read-only) per il mirror finanziario del portale AX.360.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class LedgerController : ControllerBase
    {
        private readonly IDbOdbcService _dbOdbcService;
        private readonly ILogger<LedgerController> _logger;

        public LedgerController(IDbOdbcService dbOdbcService, ILogger<LedgerController> logger)
        {
            _dbOdbcService = dbOdbcService;
            _logger = logger;
        }

        /// <summary>
        /// Movimenti di partitario, opzionalmente filtrati per cliente (CardCode) e data minima.
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<ErpLedgerEntryDto>>> Get(
            [FromQuery] string? customerCode, [FromQuery] DateTime? since)
        {
            try
            {
                var list = await _dbOdbcService.GetLedgerAsync(customerCode, since);
                return Ok(list);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving ledger (customer={Customer}, since={Since})", customerCode, since);
                return StatusCode(500, "Errore interno del server durante il recupero del partitario");
            }
        }
    }
}
