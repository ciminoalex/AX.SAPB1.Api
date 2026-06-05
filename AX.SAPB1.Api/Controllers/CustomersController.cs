using Microsoft.AspNetCore.Mvc;
using AX.SAPB1.Api.Models;
using AX.SAPB1.Api.Services;

namespace AX.SAPB1.Api.Controllers
{
    /// <summary>
    /// Profili anagrafici clienti estesi (ERP-neutri), read-only, per il mirror ExternalCustomerProfile del portale AX.
    /// Distinto da /api/lookup/customers (lista minimale usata dal bind): qui i campi anagrafici/finanziari estesi.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class CustomersController : ControllerBase
    {
        private readonly IDbOdbcService _dbOdbcService;
        private readonly ILogger<CustomersController> _logger;

        public CustomersController(IDbOdbcService dbOdbcService, ILogger<CustomersController> logger)
        {
            _dbOdbcService = dbOdbcService;
            _logger = logger;
        }

        /// <summary>Profili anagrafici estesi di tutti i clienti (bulk). Usato dal sync periodico del portale.</summary>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<CustomerProfile>>> GetProfiles()
        {
            try
            {
                return Ok(await _dbOdbcService.GetCustomerProfilesAsync());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving customer profiles");
                return StatusCode(500, "Errore interno del server durante il recupero dei profili cliente");
            }
        }

        /// <summary>Profilo anagrafico esteso di un singolo cliente. 404 se il CardCode non esiste.</summary>
        [HttpGet("{cardCode}")]
        public async Task<ActionResult<CustomerProfile>> GetProfile([FromRoute] string cardCode)
        {
            try
            {
                var profile = await _dbOdbcService.GetCustomerProfileAsync(cardCode);
                return profile == null ? NotFound() : Ok(profile);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving customer profile {CardCode}", cardCode);
                return StatusCode(500, "Errore interno del server durante il recupero del profilo cliente");
            }
        }
    }
}
