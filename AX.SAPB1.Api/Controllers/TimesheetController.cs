using Microsoft.AspNetCore.Mvc;
using AX.SAPB1.Api.Models;
using AX.SAPB1.Api.Services;

namespace AX.SAPB1.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TimesheetController : ControllerBase
    {
        private readonly IDbOdbcService _dbOdbcService;
        private readonly ISapB1ServiceLayerService _sapB1Service;
        private readonly ILogger<TimesheetController> _logger;

        public TimesheetController(
            IDbOdbcService dbOdbcService,
            ISapB1ServiceLayerService sapB1Service,
            ILogger<TimesheetController> logger)
        {
            _dbOdbcService = dbOdbcService;
            _sapB1Service = sapB1Service;
            _logger = logger;
        }

        /// <summary>
        /// Ottiene tutti i timesheet dal database
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Timesheet>>> GetTimesheets()
        {
            try
            {
                var timesheets = await _dbOdbcService.GetTimesheetsAsync();
                return Ok(timesheets);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving timesheets");
                return StatusCode(500, "Errore interno del server durante il recupero dei timesheet");
            }
        }

        /// <summary>
        /// Ottiene un timesheet specifico per DocEntry dal database
        /// </summary>
        [HttpGet("{docEntry}")]
        public async Task<ActionResult<Timesheet>> GetTimesheet(int docEntry)
        {
            try
            {
                var timesheet = await _dbOdbcService.GetTimesheetByIdAsync(docEntry);
                
                if (timesheet == null)
                    return NotFound($"Timesheet con DocEntry {docEntry} non trovato");
                
                return Ok(timesheet);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving timesheet with DocEntry {DocEntry}", docEntry);
                return StatusCode(500, "Errore interno del server durante il recupero del timesheet");
            }
        }

        /// <summary>
        /// Ottiene i timesheet per dipendente dal database
        /// </summary>
        [HttpGet("employee/{employeeId}")]
        public async Task<ActionResult<IEnumerable<Timesheet>>> GetTimesheetsByEmployee(string employeeId)
        {
            try
            {
                var timesheets = await _dbOdbcService.GetTimesheetsByEmployeeAsync(employeeId);
                return Ok(timesheets);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving timesheets for employee {EmployeeId}", employeeId);
                return StatusCode(500, "Errore interno del server durante il recupero dei timesheet per dipendente");
            }
        }

        /// <summary>
        /// Ottiene i timesheet per dipendente in un intervallo di date dal database
        /// </summary>
        [HttpGet("employee/{employeeId}/daterange")]
        public async Task<ActionResult<IEnumerable<Timesheet>>> GetTimesheetsByEmployeeAndDateRange(
            string employeeId,
            [FromQuery] DateTime startDate, 
            [FromQuery] DateTime endDate)
        {
            try
            {
                var timesheets = await _dbOdbcService.GetTimesheetsByEmployeeAndDateRangeAsync(employeeId, startDate, endDate);
                return Ok(timesheets);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving timesheets for employee {EmployeeId} in date range {StartDate} to {EndDate}", employeeId, startDate, endDate);
                return StatusCode(500, "Errore interno del server durante il recupero dei timesheet per dipendente e intervallo di date");
            }
        }

        /// <summary>
        /// Ottiene i timesheet per progetto dal database
        /// </summary>
        [HttpGet("project/{projectId}")]
        public async Task<ActionResult<IEnumerable<Timesheet>>> GetTimesheetsByProject(string projectId)
        {
            try
            {
                var timesheets = await _dbOdbcService.GetTimesheetsByProjectAsync(projectId);
                return Ok(timesheets);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving timesheets for project {ProjectId}", projectId);
                return StatusCode(500, "Errore interno del server durante il recupero dei timesheet per progetto");
            }
        }

        /// <summary>
        /// Ottiene i timesheet per intervallo di date dal database
        /// </summary>
        [HttpGet("daterange")]
        public async Task<ActionResult<IEnumerable<Timesheet>>> GetTimesheetsByDateRange(
            [FromQuery] DateTime startDate, 
            [FromQuery] DateTime endDate)
        {
            try
            {
                var timesheets = await _dbOdbcService.GetTimesheetsByDateRangeAsync(startDate, endDate);
                return Ok(timesheets);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving timesheets for date range {StartDate} to {EndDate}", startDate, endDate);
                return StatusCode(500, "Errore interno del server durante il recupero dei timesheet per intervallo di date");
            }
        }

        /// <summary>
        /// Ottiene il totale delle ore per progetto e attività
        /// </summary>
        [HttpGet("activity-time-tot")]
        public async Task<ActionResult<ActivityTimeTotal>> GetActivityTimeTot(
            [FromQuery] string projectId,
            [FromQuery] string activityId)
        {
            try
            {
                var result = await _dbOdbcService.GetActivityTimeTotAsync(projectId, activityId);
                if (result == null)
                    return NotFound();
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving activity time total for project {ProjectId} and activity {ActivityId}", projectId, activityId);
                return StatusCode(500, "Errore interno del server durante il recupero del totale ore per attività");
            }
        }

        /// <summary>
        /// Crea un nuovo timesheet tramite SAP Business One Service Layer
        /// </summary>
        [HttpPost]
        public async Task<ActionResult<Timesheet>> CreateTimesheet([FromBody] TimesheetCreateRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ModelState);

                var timesheet = await _sapB1Service.CreateTimesheetAsync(request);
                return CreatedAtAction(nameof(GetTimesheet), new { docEntry = timesheet.DocEntry }, timesheet);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating timesheet");
                return StatusCode(500, "Errore interno del server durante la creazione del timesheet");
            }
        }

        /// <summary>
        /// Crea un nuovo timesheet partendo da un payload semplificato
        /// </summary>
        [HttpPost("lite")]
        public async Task<ActionResult<Timesheet>> CreateTimesheetLite([FromBody] TimesheetCreateRequestLite request)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ModelState);

                if (request.Hours is null || request.Hours <= 0)
                    return BadRequest("Il campo Hours deve essere maggiore di zero");

                var dependencyResult = await ResolveLiteDependenciesAsync(request);
                if (dependencyResult.ErrorResult is not null)
                    return dependencyResult.ErrorResult;

                var timesheet = await _sapB1Service.CreateTimesheetLiteAsync(
                    request,
                    dependencyResult.Project!,
                    dependencyResult.Activity!);
                return CreatedAtAction(nameof(GetTimesheet), new { docEntry = timesheet.DocEntry }, timesheet);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating lite timesheet for project {Project} and activity {ActivityId}", request.Project, request.ActivityId);
                return StatusCode(500, "Errore interno del server durante la creazione del timesheet lite");
            }
        }

        /// <summary>
        /// Restituisce il payload mappato verso Service Layer senza creare il timesheet
        /// </summary>
        [HttpPost("lite/preview")]
        public async Task<ActionResult<TimesheetServiceLayerPayload>> PreviewTimesheetLiteMapping([FromBody] TimesheetCreateRequestLite request)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ModelState);

                if (request.Hours is null || request.Hours <= 0)
                    return BadRequest("Il campo Hours deve essere maggiore di zero");

                var dependencyResult = await ResolveLiteDependenciesAsync(request);
                if (dependencyResult.ErrorResult is not null)
                    return dependencyResult.ErrorResult;

                var payload = _sapB1Service.BuildTimesheetLitePayload(
                    request,
                    dependencyResult.Project!,
                    dependencyResult.Activity!);

                return Ok(payload);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error previewing lite timesheet mapping for project {Project} and activity {ActivityId}", request.Project, request.ActivityId);
                return StatusCode(500, "Errore interno del server durante il preview del mapping timesheet lite");
            }
        }

        private async Task<(ProjectLookupDetail? Project, ActivitySummary? Activity, ActionResult? ErrorResult)> ResolveLiteDependenciesAsync(TimesheetCreateRequestLite request)
        {
            var project = await _dbOdbcService.GetProjectLookupDetailByCodeAsync(request.Project);
            if (project is null)
                return (null, null, NotFound($"Progetto {request.Project} non trovato"));

            var activities = await _dbOdbcService.GetActivitiesByProjectAsync(request.Project);
            var activity = activities.FirstOrDefault(a =>
                string.Equals(a.Code, request.ActivityId, StringComparison.OrdinalIgnoreCase));

            if (activity is null)
                return (null, null, NotFound($"Attività {request.ActivityId} non trovata per il progetto {request.Project}"));

            if (string.IsNullOrWhiteSpace(project.CardCode) || string.IsNullOrWhiteSpace(project.Name))
                return (null, null, BadRequest($"Dati anagrafici incompleti per il progetto {request.Project}"));

            return (project, activity, null);
        }

        /// <summary>
        /// Aggiorna un timesheet esistente tramite SAP Business One Service Layer
        /// </summary>
        [HttpPut("{docEntry}")]
        public async Task<ActionResult<Timesheet>> UpdateTimesheet(int docEntry, [FromBody] TimesheetUpdateRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ModelState);

                if (request.DocEntry != docEntry)
                    return BadRequest("Il DocEntry nell'URL non corrisponde a quello nel body della richiesta");

                var timesheet = await _sapB1Service.UpdateTimesheetAsync(request);
                return Ok(timesheet);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating timesheet with DocEntry {DocEntry}", docEntry);
                return StatusCode(500, "Errore interno del server durante l'aggiornamento del timesheet");
            }
        }

        /// <summary>
        /// Elimina un timesheet tramite SAP Business One Service Layer
        /// </summary>
        [HttpDelete("{code}")]
        public async Task<ActionResult> DeleteTimesheet(string code)
        {
            try
            {
                var result = await _sapB1Service.DeleteTimesheetAsync(code);
                
                if (result)
                    return NoContent();
                else
                    return NotFound($"Timesheet con Code {code} non trovato o non eliminabile");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting timesheet with Code {code}", code);
                return StatusCode(500, "Errore interno del server durante l'eliminazione del timesheet");
            }
        }
    }
}
