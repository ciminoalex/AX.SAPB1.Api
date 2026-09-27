using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using AX.SAPB1.Api.Models;
using AX.SAPB1.Api.Services;
using AX.SAPB1.Api.Services.SalesDocuments;
using AX.SAPB1.Api.Services.Timesheets;
using AX.SAPB1.Api.Support;

namespace AX.SAPB1.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TimesheetController : ControllerBase
    {
        // Serializza, dentro il processo, gli aggiornamenti ore della STESSA riga: "leggi lo stato, decidi,
        // scrivi" non è atomico, e due PATCH concorrenti del portale sulla stessa riga non devono intrecciarsi.
        // Da chi scrive in SAP nel frattempo (SGS che fattura, un utente) protegge invece la rilettura della riga
        // dal Service Layer subito prima del PATCH: vedi il doc dell'azione.
        private static readonly KeyedAsyncLock HoursLocks = new();

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
        /// Stato di fatturazione delle righe di timesheet nella finestra [from, to], con la fattura che le
        /// porta quando esiste. Sola lettura: non scrive mai verso SAP. Contratto ERP-neutro, consumato dal
        /// sync del portale (nessun nome SAP nella risposta).
        /// <para>
        /// Porta anche la chiave di riserva per l'abbinamento per attributi (risorsa, progetto, attività,
        /// data) — vedi <see cref="TimesheetBillingState"/> — per le righe del portale che non hanno
        /// mai ricevuto l'identificativo SAP al momento del push.
        /// </para>
        /// </summary>
        [HttpGet("billing-state")]
        public async Task<ActionResult<IEnumerable<TimesheetBillingState>>> GetBillingState(
            [FromQuery] DateTime from,
            [FromQuery] DateTime to)
        {
            if (from == default || to == default)
                return BadRequest("Parametri 'from' e 'to' obbligatori.");
            if (to < from)
                return BadRequest("'to' non può precedere 'from'.");
            // Cap difensivo: oltre 400 giorni la finestra non ha più senso per un sync incrementale e la
            // risposta rischia di non stare comodamente nel timeout del chiamante.
            if ((to - from).TotalDays > 400)
                return BadRequest("La finestra 'from'-'to' non può superare 400 giorni.");

            try { return Ok(await _dbOdbcService.GetTimesheetBillingStatesAsync(from, to)); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving timesheet billing states for date range {From} to {To}", from, to);
                return StatusCode(500, "Errore interno del server durante il recupero dello stato di fatturazione dei timesheet");
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

                var billableError = TimesheetHoursRules.ValidateBillableHours(request.Hours.Value, request.BillableHours);
                if (billableError is not null)
                    return BadRequest(billableError);

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

                var billableError = TimesheetHoursRules.ValidateBillableHours(request.Hours.Value, request.BillableHours);
                if (billableError is not null)
                    return BadRequest(billableError);

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
        /// Aggiorna le ore lorde e fatturabili di una riga di timesheet già spinta dal portale e non ancora
        /// fatturata: <c>U_TimeNrTot = hours</c>, <c>U_TimeNrNet = billableHours</c> (la quantità che SGS fattura),
        /// <c>U_TimeNrNF = hours - billableHours</c>, e nient'altro.
        /// <para>
        /// È il lato servizio di un merge a tre vie (base = ultimo valore spinto, nostro = portale, loro = SAP): la
        /// decisione è in <see cref="TimesheetHoursRules.Decide"/> (pura, testata). Risposte:
        /// 400 <c>invalid</c>; 404 <c>not_found</c>; 409 <c>canceled</c>; 409 <c>billed</c> (<c>U_Status</c>
        /// «Fatturato» o <c>U_DestEntry</c> valorizzato: le righe fatturate non si toccano MAI); 200
        /// <c>unchanged</c> (SAP ha già quei valori: un nuovo tentativo dopo un timeout riuscito non scrive due
        /// volte); 409 <c>changed_in_erp</c> (con <c>expected*</c> presenti e diversi da SAP: modificata a mano, non
        /// si sovrascrive); 200 <c>updated</c>; 502 <c>error</c> se il Service Layer rifiuta la ricerca o la scrittura.
        /// </para>
        /// <para>
        /// <b>Due letture, una sola decisione che conta.</b> La prima decisione si prende sul dato ODBC: costa poco e
        /// risponde a <c>not_found</c>/<c>billed</c>/<c>unchanged</c>/... senza aprire una sessione del Service Layer.
        /// Se dice «scrivi», il Service Layer rilegge la riga e ridecide subito prima del PATCH
        /// (<see cref="ISapB1ServiceLayerService.UpdateTimesheetHoursAsync"/>): la validazione della sessione o un
        /// login possono durare a lungo, e una riga fatturata in quell'intervallo non deve essere sovrascritta. Resta
        /// scoperto solo il giro fra quella rilettura e il PATCH. Il lock per riga serializza le richieste di questo
        /// processo, non chi scrive direttamente in SAP.
        /// </para>
        /// <para>
        /// <b>Chiamante andato via.</b> Se il portale abbandona la richiesta (timeout) prima del PATCH, non si scrive:
        /// una scrittura arrivata tardi farebbe sembrare al suo tentativo successivo la riga modificata a mano in SAP.
        /// Resta la corsa inevitabile "abbandono dopo l'invio del PATCH", che il ramo <c>unchanged</c> copre quando il
        /// nuovo tentativo porta gli stessi valori.
        /// </para>
        /// </summary>
        [HttpPatch("{docEntry:int}/hours")]
        public async Task<ActionResult<TimesheetHoursUpdateResult>> UpdateTimesheetHours(int docEntry, [FromBody] TimesheetHoursUpdateRequest request)
        {
            var validationError = TimesheetHoursRules.ValidateUpdate(request);
            if (validationError is not null)
                return BadRequest(new TimesheetHoursUpdateResult { Outcome = TimesheetHoursOutcome.Invalid, Message = validationError });

            var cancellationToken = HttpContext?.RequestAborted ?? CancellationToken.None;
            try
            {
                using var rowLock = await HoursLocks.AcquireAsync(docEntry.ToString(CultureInfo.InvariantCulture), cancellationToken);

                var current = await _dbOdbcService.GetTimesheetHoursStateAsync(docEntry);
                var decision = TimesheetHoursRules.Decide(current, request);

                if (!decision.ShouldWrite)
                    return NoWrite(docEntry, request, decision, recheck: false);

                // La lettura ODBC può aver atteso a lungo il lock o il database: se il portale se n'è già andato,
                // non si apre nemmeno la sessione del Service Layer.
                cancellationToken.ThrowIfCancellationRequested();
                var write = await _sapB1Service.UpdateTimesheetHoursAsync(docEntry, request, current, cancellationToken);

                if (write.Superseded is { } superseded)
                    return NoWrite(docEntry, request, superseded, recheck: true);

                var before = write.Fresh ?? current!;
                var response = write.Response!;
                if (!response.IsSuccess)
                {
                    var sapMessage = ServiceLayerErrors.ExtractMessage(response.Body);
                    _logger.LogError(
                        "Timesheet DocEntry {DocEntry}: il Service Layer ha rifiutato l'aggiornamento ore a {Hours}/{BillableHours} (HTTP {Status}): {SapMessage}",
                        docEntry, request.Hours, request.BillableHours, response.StatusCode, sapMessage);
                    return StatusCode(StatusCodes.Status502BadGateway, new TimesheetHoursUpdateResult
                    {
                        Outcome = TimesheetHoursOutcome.Error,
                        CurrentHours = before.TotalHours,
                        CurrentBillableHours = before.BillableHours,
                        Message = $"SAP ha rifiutato l'aggiornamento ({response.StatusCode}): {sapMessage}",
                    });
                }

                _logger.LogInformation(
                    "Timesheet DocEntry {DocEntry}: ore aggiornate da {FromHours}/{FromBillableHours} a {ToHours}/{ToBillableHours} (lorde/fatturabili)",
                    docEntry, before.TotalHours, before.BillableHours, decision.CurrentHours, decision.CurrentBillableHours);
                return Ok(decision.ToResult());
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Nessuno legge questa risposta: il portale ha già smesso di aspettare. Conta il log, e conta che in
                // SAP non sia partito niente.
                _logger.LogInformation(
                    "Timesheet DocEntry {DocEntry}: aggiornamento ore a {Hours}/{BillableHours} abbandonato dal chiamante prima della scrittura, SAP non toccato",
                    docEntry, request.Hours, request.BillableHours);
                return StatusCode(StatusCodes.Status499ClientClosedRequest, new TimesheetHoursUpdateResult
                {
                    Outcome = TimesheetHoursOutcome.Error,
                    Message = "Richiesta abbandonata dal chiamante prima della scrittura: ore non aggiornate",
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating hours of timesheet with DocEntry {DocEntry}", docEntry);
                return StatusCode(500, "Errore interno del server durante l'aggiornamento delle ore del timesheet");
            }
        }

        /// <summary>
        /// Risposta di un aggiornamento ore che non scrive, con il suo log: Warning per i rifiuti (409), Information
        /// per il resto. <paramref name="recheck"/> dice che a fermare la scrittura è stata la rilettura dal Service
        /// Layer subito prima del PATCH, cioè che la riga è cambiata in SAP DOPO la lettura ODBC: un caso raro, da
        /// riconoscere nel log.
        /// </summary>
        private ObjectResult NoWrite(int docEntry, TimesheetHoursUpdateRequest request, TimesheetHoursDecision decision, bool recheck)
        {
            var when = recheck ? "alla rilettura prima della scrittura" : "sul dato letto";
            if (decision.HttpStatus == StatusCodes.Status409Conflict)
                _logger.LogWarning(
                    "Timesheet DocEntry {DocEntry}: aggiornamento ore a {Hours}/{BillableHours} rifiutato {When} ({Outcome}); in SAP {CurrentHours}/{CurrentBillableHours}",
                    docEntry, request.Hours, request.BillableHours, when, decision.Outcome, decision.CurrentHours, decision.CurrentBillableHours);
            else
                _logger.LogInformation(
                    "Timesheet DocEntry {DocEntry}: aggiornamento ore a {Hours}/{BillableHours} senza scrittura {When} ({Outcome})",
                    docEntry, request.Hours, request.BillableHours, when, decision.Outcome);
            return StatusCode(decision.HttpStatus, decision.ToResult());
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
