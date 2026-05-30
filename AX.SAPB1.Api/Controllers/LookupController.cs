using Microsoft.AspNetCore.Mvc;
using AX.SAPB1.Api.Models;
using AX.SAPB1.Api.Services;

namespace AX.SAPB1.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LookupController : ControllerBase
    {
        private readonly IDbOdbcService _dbOdbcService;
        private readonly ILogger<LookupController> _logger;

        public LookupController(IDbOdbcService dbOdbcService, ILogger<LookupController> logger)
        {
            _dbOdbcService = dbOdbcService;
            _logger = logger;
        }

        /// <summary>
        /// Elenco clienti (CardCode e CardName)
        /// </summary>
        [HttpGet("customers")]
        public async Task<ActionResult<IEnumerable<CustomerSummary>>> GetCustomers()
        {
            try
            {
                var list = await _dbOdbcService.GetCustomersAsync();
                return Ok(list);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving customers");
                return StatusCode(500, "Errore interno del server durante il recupero dei clienti");
            }
        }

        /// <summary>
        /// Elenco referenti per cliente (codice e nome)
        /// </summary>
        [HttpGet("customers/{cardCode}/contacts")]
        public async Task<ActionResult<IEnumerable<ContactSummary>>> GetContactsByCustomer([FromRoute] string cardCode)
        {
            try
            {
                var list = await _dbOdbcService.GetContactsByCustomerAsync(cardCode);
                return Ok(list);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving contacts for customer {CardCode}", cardCode);
                return StatusCode(500, "Errore interno del server durante il recupero dei referenti");
            }
        }

        /// <summary>
        /// Elenco progetti (codice e descrizione)
        /// </summary>
        [HttpGet("projects")]
        public async Task<ActionResult<IEnumerable<ProjectSummary>>> GetProjects()
        {
            try
            {
                var list = await _dbOdbcService.GetProjectsAsync();
                return Ok(list);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving projects");
                return StatusCode(500, "Errore interno del server durante il recupero dei progetti");
            }
        }

        /// <summary>
        /// Elenco attività per progetto (codice attività e descrizione)
        /// </summary>
        [HttpGet("projects/{projectCode}/activities")]
        public async Task<ActionResult<IEnumerable<ActivitySummary>>> GetActivitiesByProject([FromRoute] string projectCode)
        {
            try
            {
                var list = await _dbOdbcService.GetActivitiesByProjectAsync(projectCode);
                return Ok(list);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving activities for project {ProjectCode}", projectCode);
                return StatusCode(500, "Errore interno del server durante il recupero delle attività");
            }
        }

        /// <summary>
        /// Elenco progetti per cliente (codice e descrizione)
        /// </summary>
        [HttpGet("customers/{cardCode}/projects")]
        public async Task<ActionResult<IEnumerable<ProjectSummary>>> GetProjectsByCustomer([FromRoute] string cardCode)
        {
            try
            {
                var list = await _dbOdbcService.GetProjectsByCustomerAsync(cardCode);
                return Ok(list);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving projects for customer {CardCode}", cardCode);
                return StatusCode(500, "Errore interno del server durante il recupero dei progetti per cliente");
            }
        }

        /// <summary>
        /// Elenco risorse (dipendenti attivi)
        /// </summary>
        [HttpGet("resources")]
        public async Task<ActionResult<IEnumerable<ResourceSummary>>> GetResources()
        {
            try
            {
                var list = await _dbOdbcService.GetResourcesAsync();
                return Ok(list);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving resources");
                return StatusCode(500, "Errore interno del server durante il recupero delle risorse");
            }
        }
    }
}


