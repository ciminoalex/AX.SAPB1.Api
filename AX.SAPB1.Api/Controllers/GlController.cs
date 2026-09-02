using AX.SAPB1.Api.Models;
using AX.SAPB1.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AX.SAPB1.Api.Controllers
{
    /// <summary>
    /// Contabilità generale: conto economico, anagrafiche (conti, periodi, progetti contabili, dimensioni)
    /// e scrittura dell'attribuzione analitica sulle righe contabili.
    ///
    /// <para>Da non confondere con <see cref="LedgerController"/>, che è il partitario CLIENTI. Il nome
    /// "ledger" copre due cose diverse in SAP e la confusione costa cara: lì c'è lo scadenzario, qui il
    /// conto economico riga per riga.</para>
    /// </summary>
    [ApiController]
    [Route("api/gl")]
    public class GlController : ControllerBase
    {
        private readonly IDbOdbcService _db;
        private readonly ILogger<GlController> _logger;

        public GlController(IDbOdbcService db, ILogger<GlController> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>Piano dei conti, limitato al conto economico.</summary>
        [HttpGet("accounts")]
        public async Task<ActionResult<IEnumerable<GlAccountDto>>> GetAccounts()
        {
            try { return Ok(await _db.GetGlAccountsAsync()); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Errore nel recupero del piano dei conti.");
                return StatusCode(500, "Errore interno durante il recupero del piano dei conti");
            }
        }

        /// <summary>
        /// Fattura/NC di origine di una registrazione JDT1, per l'anteprima nella riconciliazione del portale:
        /// dalla descrizione del conto non sempre si capisce cosa si è comprato/venduto, le righe del
        /// documento sì. <paramref name="docType"/> è il tipo neutro (fattura_vendita/…), decide la tabella.
        /// 204 se non c'è un documento con righe (giornale manuale, pagamento) o non esiste.
        /// </summary>
        [HttpGet("source-document")]
        public async Task<ActionResult<ErpInvoiceDto>> GetSourceDocument([FromQuery] int transId, [FromQuery] string? docType)
        {
            if (transId <= 0) return BadRequest("Parametro 'transId' obbligatorio.");
            try
            {
                var doc = await _db.GetSourceDocumentAsync(transId, docType);
                return doc == null ? NotFound() : Ok(doc);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Errore nel recupero della fattura di origine (transId {TransId}, docType {DocType}).", transId, docType);
                return StatusCode(500, "Errore interno durante il recupero della fattura di origine");
            }
        }

        /// <summary>Periodi contabili: definiscono l'anno fiscale e quali periodi sono aperti.</summary>
        [HttpGet("periods")]
        public async Task<ActionResult<IEnumerable<GlFiscalPeriodDto>>> GetPeriods()
        {
            try { return Ok(await _db.GetGlFiscalPeriodsAsync()); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Errore nel recupero dei periodi contabili.");
                return StatusCode(500, "Errore interno durante il recupero dei periodi contabili");
            }
        }

        /// <summary>Anagrafica dei progetti contabili: i codici scrivibili sulla riga contabile.</summary>
        [HttpGet("fiscal-projects")]
        public async Task<ActionResult<IEnumerable<GlFiscalProjectDto>>> GetFiscalProjects()
        {
            try { return Ok(await _db.GetGlFiscalProjectsAsync()); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Errore nel recupero dei progetti contabili.");
                return StatusCode(500, "Errore interno durante il recupero dei progetti contabili");
            }
        }

        /// <summary>Dimensioni analitiche configurate nell'ERP, con il flag di attivazione.</summary>
        [HttpGet("dimensions")]
        public async Task<ActionResult<IEnumerable<GlDimensionDto>>> GetDimensions()
        {
            try { return Ok(await _db.GetGlDimensionsAsync()); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Errore nel recupero delle dimensioni analitiche.");
                return StatusCode(500, "Errore interno durante il recupero delle dimensioni analitiche");
            }
        }

        /// <summary>Centri di costo valorizzabili, per dimensione.</summary>
        [HttpGet("distribution-rules")]
        public async Task<ActionResult<IEnumerable<GlDistributionRuleDto>>> GetDistributionRules()
        {
            try { return Ok(await _db.GetGlDistributionRulesAsync()); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Errore nel recupero dei centri di costo.");
                return StatusCode(500, "Errore interno durante il recupero dei centri di costo");
            }
        }

        /// <summary>
        /// Righe di conto economico registrate nella finestra indicata (per data di registrazione).
        /// Il chiamante rilegge la finestra intera e fa mark-and-sweep: non esiste un incrementale, perché
        /// le registrazioni vengono modificate in place e cancellate senza lasciare traccia in una data.
        /// Paginabile con <paramref name="skip"/>/<paramref name="take"/>: <c>take = 0</c> (default) significa
        /// nessuna paginazione, per retrocompatibilità con il chiamante in produzione.
        /// </summary>
        [HttpGet("lines")]
        public async Task<ActionResult<IEnumerable<GlLineDto>>> GetLines(
            [FromQuery] DateTime from,
            [FromQuery] DateTime to,
            [FromQuery] int skip = 0,
            [FromQuery] int take = 0)
        {
            if (from == default || to == default)
                return BadRequest("Parametri 'from' e 'to' obbligatori.");
            if (to < from)
                return BadRequest("'to' non può precedere 'from'.");
            if (skip < 0)
                return BadRequest("skip non può essere negativo.");
            if (take < 0)
                return BadRequest("take non può essere negativo.");
            // Cap difensivo: take=0 significa "finestra intera" (comportamento storico, che il
            // portale in produzione usa oggi); oltre 5000 righe per pagina la risposta non sta
            // comodamente in memoria né nel timeout del chiamante.
            if (take > 5000) take = 5000;

            try { return Ok(await _db.GetGlLinesAsync(from, to, skip, take)); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Errore nel recupero delle righe di contabilità ({From} → {To}).", from, to);
                return StatusCode(500, "Errore interno durante il recupero delle righe di contabilità");
            }
        }

        /// <summary>Rilettura mirata di registrazioni specifiche: chiude il cerchio dopo una scrittura.</summary>
        [HttpPost("lines/by-entry-ids")]
        public async Task<ActionResult<IEnumerable<GlLineDto>>> GetLinesByEntryIds([FromBody] List<int> entryIds)
        {
            if (entryIds == null || entryIds.Count == 0)
                return BadRequest("Elenco di registrazioni vuoto.");

            try { return Ok(await _db.GetGlLinesByEntryIdsAsync(entryIds)); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Errore nella rilettura di {Count} registrazioni.", entryIds.Count);
                return StatusCode(500, "Errore interno durante la rilettura delle registrazioni");
            }
        }

        /// <summary>
        /// Attribuisce progetto e dimensioni analitiche a una o più righe contabili, in una sola
        /// transazione: o passano tutte o non passa nessuna.
        ///
        /// <para>Esiti possibili per riga: aggiornata, <b>in conflitto</b> (qualcuno ha modificato la riga
        /// dal client SAP dopo che il chiamante l'aveva letta — va riletta e riproposta), o non trovata.
        /// I valori restituiti sono sempre riletti dal DB, mai l'eco della richiesta.</para>
        /// </summary>
        [HttpPost("attribution")]
        public async Task<ActionResult<GlAttributionResult>> UpdateAttribution([FromBody] GlAttributionRequest request)
        {
            if (request?.Items == null || request.Items.Count == 0)
                return BadRequest("Nessuna riga da aggiornare.");

            if (request.Items.Any(i => i.ErpEntryId <= 0))
                return BadRequest("Ogni riga richiede un ErpEntryId valido.");

            try
            {
                var result = await _db.UpdateGlAttributionAsync(request);

                // Codice inesistente in anagrafica: colpa del chiamante, e nulla è stato scritto.
                if (!result.Success && result.InvalidCodes.Count > 0)
                    return BadRequest(result);

                if (!result.Success)
                    return StatusCode(500, result);

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Errore nell'attribuzione analitica di {Count} righe.", request.Items.Count);
                return StatusCode(500, "Errore interno durante l'attribuzione analitica");
            }
        }
    }
}
