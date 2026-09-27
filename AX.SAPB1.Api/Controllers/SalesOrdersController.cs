using AX.SAPB1.Api.Models;
using AX.SAPB1.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AX.SAPB1.Api.Controllers
{
    /// <summary>
    /// Ordini cliente nati dal portale AX.360 (<c>U_AX360_InvId</c> valorizzato), per il sync
    /// <c>sales_orders</c> del portale: stato (open/closed/cancelled), residuo imponibile e fatture tratte
    /// dall'ordine. Sola lettura.
    /// </summary>
    [ApiController]
    [Route("api/sales-orders")]
    public class SalesOrdersController : ControllerBase
    {
        private readonly IDbOdbcService _db;
        private readonly ILogger<SalesOrdersController> _logger;

        public SalesOrdersController(IDbOdbcService db, ILogger<SalesOrdersController> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>
        /// Con <paramref name="since"/> (yyyy-MM-dd) solo gli ordini modificati da quella data
        /// (<c>ORDR.UpdateDate</c>): una fattura tratta dall'ordine lo aggiorna, quindi rientra nel delta.
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<ErpSalesOrderDto>>> Get([FromQuery] DateTime? since)
        {
            try
            {
                return Ok(await _db.GetSalesOrdersAsync(since));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Errore nella lettura degli ordini cliente (since={Since})", since);
                return StatusCode(500, "Errore interno del server durante il recupero degli ordini cliente");
            }
        }
    }
}
