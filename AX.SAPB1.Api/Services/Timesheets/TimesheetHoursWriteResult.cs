using AX.SAPB1.Api.Models;
using AX.SAPB1.Api.Services.SalesDocuments;

namespace AX.SAPB1.Api.Services.Timesheets
{
    /// <summary>
    /// Esito di <see cref="ISapB1ServiceLayerService.UpdateTimesheetHoursAsync"/>. Tre casi che si escludono:
    /// <list type="bullet">
    /// <item><see cref="Superseded"/> valorizzato: la riga riletta dal Service Layer subito prima del PATCH non va più
    /// scritta (fatturata, annullata o modificata in SAP dopo la lettura ODBC, oppure con i valori già giusti). Il
    /// PATCH <b>non</b> è partito e <see cref="Superseded"/> è la risposta da dare al portale.</item>
    /// <item><see cref="Response"/> con <c>IsSuccess</c>: PATCH eseguito.</item>
    /// <item><see cref="Response"/> senza <c>IsSuccess</c>: la ricerca della riga o il PATCH sono stati rifiutati
    /// dal Service Layer (esito grezzo, da tradurre in 502).</item>
    /// </list>
    /// </summary>
    public sealed class TimesheetHoursWriteResult
    {
        private TimesheetHoursWriteResult(TimesheetHoursDecision? superseded, ServiceLayerResponse? response, TimesheetHoursState? fresh)
        {
            Superseded = superseded;
            Response = response;
            Fresh = fresh;
        }

        /// <summary>Decisione ripresa sulla riga appena riletta, quando non è più «updated». Null se si è tentato di scrivere.</summary>
        public TimesheetHoursDecision? Superseded { get; }

        /// <summary>Esito grezzo dell'ultima chiamata al Service Layer (ricerca fallita o PATCH). Null se <see cref="Superseded"/>.</summary>
        public ServiceLayerResponse? Response { get; }

        /// <summary>
        /// Stato della riga come l'ha restituito il Service Layer subito prima del PATCH: sono i valori "da" del log di
        /// un aggiornamento. Null se la ricerca è fallita o non ha trovato la riga.
        /// </summary>
        public TimesheetHoursState? Fresh { get; }

        /// <summary>La riga riletta non si scrive: <paramref name="decision"/> è la risposta da dare, nessun PATCH inviato.</summary>
        public static TimesheetHoursWriteResult NotWritten(TimesheetHoursDecision decision, TimesheetHoursState fresh)
        {
            ArgumentNullException.ThrowIfNull(decision);
            if (decision.ShouldWrite)
                throw new ArgumentException("Una decisione che prevede la scrittura non può fermare il PATCH.", nameof(decision));
            return new(decision, null, fresh);
        }

        /// <summary>Esito di una chiamata al Service Layer (PATCH eseguito, oppure ricerca/PATCH rifiutati).</summary>
        public static TimesheetHoursWriteResult FromResponse(ServiceLayerResponse response, TimesheetHoursState? fresh)
        {
            ArgumentNullException.ThrowIfNull(response);
            return new(null, response, fresh);
        }
    }
}
