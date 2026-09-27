using System.Globalization;
using AX.SAPB1.Api.Models;

namespace AX.SAPB1.Api.Services.Timesheets
{
    /// <summary>Valori di <see cref="TimesheetHoursUpdateResult.Outcome"/>: parte del contratto con il portale, non rinominare.</summary>
    public static class TimesheetHoursOutcome
    {
        public const string Updated = "updated";
        public const string Unchanged = "unchanged";
        public const string Canceled = "canceled";
        public const string Billed = "billed";
        public const string ChangedInErp = "changed_in_erp";
        public const string Invalid = "invalid";
        public const string NotFound = "not_found";
        public const string Error = "error";
    }

    /// <summary>
    /// Esito della decisione su un aggiornamento ore: <see cref="ShouldWrite"/> dice se scrivere in SAP,
    /// <see cref="HttpStatus"/> e il resto sono la risposta da dare (per <c>updated</c>, quella da dare DOPO una
    /// scrittura riuscita).
    /// </summary>
    public sealed record TimesheetHoursDecision(
        string Outcome,
        int HttpStatus,
        bool ShouldWrite,
        decimal? CurrentHours,
        decimal? CurrentBillableHours,
        string? Message)
    {
        public TimesheetHoursUpdateResult ToResult() => new()
        {
            Outcome = Outcome,
            CurrentHours = CurrentHours,
            CurrentBillableHours = CurrentBillableHours,
            Message = Message,
        };
    }

    /// <summary>
    /// Regole pure (nessun I/O) sulle ore di una riga di <c>@SGS_PRJ_OTMS</c>.
    /// <para>
    /// <b>Perché esistono.</b> SGS fattura il T&amp;M da <c>@SGS_PRJ_OTMS</c> usando <c>U_TimeNrNet</c> come
    /// quantità di riga (e seleziona solo le righe con <c>U_TimeNrNet &gt; 0</c>). Fino a qui il servizio
    /// scriveva <c>U_TimeNrTot = U_TimeNrNet = ore lorde</c> e <c>U_TimeNrNF = 0</c>: quando il capo progetto
    /// riduceva nel portale le ore fatturabili, SGS fatturava comunque le ore piene (caso reale: Comal,
    /// 22-23/09/2026, 6,5 h lorde e 4 fatturabili). La ripartizione corretta è
    /// <c>Tot = lorde, Net = fatturabili, NF = lorde - fatturabili</c>.
    /// </para>
    /// <para>
    /// <b>Aggiornamento come merge a tre vie</b> (base = ultimo valore spinto, nostro = portale, loro = SAP):
    /// una riga fatturata non si tocca MAI (né per stato né per documento di destinazione), una riga annullata
    /// nemmeno, e una riga modificata a mano in SAP dopo il push non si sovrascrive. L'ordine dei controlli è
    /// quello del contratto e non è arbitrario: <c>unchanged</c> viene PRIMA di <c>changed_in_erp</c>, così un
    /// nuovo tentativo dopo un timeout andato a buon fine (SAP ha già i valori nuovi, diversi dagli attesi)
    /// risponde "niente da fare" invece di "modificata da altri".
    /// </para>
    /// </summary>
    public static class TimesheetHoursRules
    {
        /// <summary>Tolleranza dei confronti fra ore (inclusa): 4 e 4,001 sono la stessa quantità.</summary>
        public const decimal Tolerance = 0.001m;

        /// <summary>Valore di <c>U_Status</c> di una riga fatturata (misurato: gli stati sono esattamente tre).</summary>
        public const string BilledStatus = "Fatturato";

        /// <summary>
        /// Vincolo sulle ore fatturabili della creazione lite, dove l'assenza vale "uguale alle ore lorde".
        /// Null se valido, altrimenti il messaggio per il 400. Presuppone <paramref name="hours"/> già
        /// verificato maggiore di zero.
        /// </summary>
        public static string? ValidateBillableHours(decimal hours, decimal? billableHours)
        {
            if (billableHours is null) return null;
            if (billableHours < 0m || billableHours > hours)
                return $"Il campo BillableHours deve essere compreso fra 0 e Hours ({Format(hours)}): ricevuto {Format(billableHours.Value)}";
            return null;
        }

        /// <summary>
        /// Validazione del body di <c>PATCH /api/timesheet/{docEntry}/hours</c>. Null se valido, altrimenti il
        /// messaggio per il 400. Qui <c>BillableHours</c> è obbligatorio (vedi <see cref="TimesheetHoursUpdateRequest.BillableHours"/>).
        /// </summary>
        public static string? ValidateUpdate(TimesheetHoursUpdateRequest? request)
        {
            if (request is null) return "Body della richiesta mancante";
            if (request.Hours is null || request.Hours <= 0m) return "Il campo hours è obbligatorio e deve essere maggiore di zero";
            if (request.BillableHours is null) return "Il campo billableHours è obbligatorio";
            if (request.BillableHours < 0m || request.BillableHours > request.Hours)
                return $"Il campo billableHours deve essere compreso fra 0 e hours ({Format(request.Hours.Value)}): ricevuto {Format(request.BillableHours.Value)}";
            return null;
        }

        /// <summary>
        /// Ripartizione delle ore nei tre campi di <c>@SGS_PRJ_OTMS</c>: <c>U_TimeNrTot = hours</c>,
        /// <c>U_TimeNrNet = billableHours ?? hours</c>, <c>U_TimeNrNF = hours - net</c>. Lancia su un valore fuori
        /// intervallo: i controller validano prima, questa è l'ultima difesa contro una riga incoerente in SAP
        /// (mai scrivere NF negativo o Net maggiore del Tot).
        /// </summary>
        public static (decimal Total, decimal Billable, decimal NonBillable) SplitHours(decimal hours, decimal? billableHours)
        {
            var billable = billableHours ?? hours;
            if (hours <= 0m)
                throw new ArgumentOutOfRangeException(nameof(hours), hours, "Le ore devono essere maggiori di zero.");
            if (billable < 0m || billable > hours)
                throw new ArgumentOutOfRangeException(nameof(billableHours), billable, "Le ore fatturabili devono essere comprese fra 0 e le ore lorde.");
            return (hours, billable, hours - billable);
        }

        /// <summary>
        /// <c>Canceled = 'Y'</c> (tollerante a spazi e maiuscole). Accetta anche <c>tYES</c>, la forma enumerata
        /// con cui il Service Layer può restituire i flag sì/no: lo stesso controllo vale sia sulla riga letta via
        /// ODBC sia su quella riletta dal Service Layer subito prima della scrittura.
        /// </summary>
        public static bool IsCanceled(string? canceled)
        {
            var text = canceled?.Trim();
            return string.Equals(text, "Y", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(text, "tYES", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Riga fatturata: <c>U_Status = 'Fatturato'</c> OPPURE <c>U_DestEntry</c> valorizzato. Basta uno dei due
        /// segnali: lo stato può non essere stato aggiornato e il documento di destinazione può mancare (misurato:
        /// righe «Fatturato» che puntano a un ordine o a una consegna), ma nessuno dei due casi rende la riga
        /// modificabile.
        /// </summary>
        public static bool IsBilled(string? status, string? destEntry)
            => string.Equals(status?.Trim(), BilledStatus, StringComparison.OrdinalIgnoreCase)
               || HasDestinationDocument(destEntry);

        /// <summary>
        /// <c>U_DestEntry</c> "valorizzato" = non null, non vuoto e diverso da 0. La colonna può arrivare come
        /// numero o come testo: il testo si legge a cultura invariante, e un testo non numerico conta come
        /// valorizzato (prudenza: nel dubbio la riga ha un documento di destinazione e non si tocca). Niente
        /// separatore delle migliaia nel parsing: con <c>NumberStyles.Number</c> un «0,0» diventerebbe 0 e la riga
        /// sembrerebbe senza documento; così invece è un testo non numerico, cioè valorizzato.
        /// </summary>
        public static bool HasDestinationDocument(string? destEntry)
        {
            var text = destEntry?.Trim();
            if (string.IsNullOrEmpty(text)) return false;
            const NumberStyles plainDecimal = NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite
                | NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
            if (decimal.TryParse(text, plainDecimal, CultureInfo.InvariantCulture, out var number))
                return number != 0m;
            return true;
        }

        /// <summary>Stessa quantità di ore entro <see cref="Tolerance"/>. Un valore assente non è mai uguale a nulla.</summary>
        public static bool SameHours(decimal? a, decimal? b)
            => a.HasValue && b.HasValue && Math.Abs(a.Value - b.Value) <= Tolerance;

        /// <summary>
        /// Decide cosa fare di un <c>PATCH /api/timesheet/{docEntry}/hours</c> dato lo stato corrente della riga
        /// in SAP (<paramref name="current"/> null = riga inesistente). Ordine del contratto: 400 validazione,
        /// 404, 409 <c>canceled</c>, 409 <c>billed</c>, 200 <c>unchanged</c>, 409 <c>changed_in_erp</c>
        /// (solo con gli attesi presenti), altrimenti scrittura e 200 <c>updated</c>.
        /// </summary>
        public static TimesheetHoursDecision Decide(TimesheetHoursState? current, TimesheetHoursUpdateRequest? request)
        {
            var validationError = ValidateUpdate(request);
            if (validationError is not null)
                return new(TimesheetHoursOutcome.Invalid, 400, false, null, null, validationError);

            if (current is null)
                return new(TimesheetHoursOutcome.NotFound, 404, false, null, null, "Riga di timesheet non trovata in SAP");

            var hours = request!.Hours!.Value;
            var billable = request.BillableHours!.Value;

            if (IsCanceled(current.Canceled))
                return new(TimesheetHoursOutcome.Canceled, 409, false, current.TotalHours, current.BillableHours,
                    "La riga di timesheet è annullata in SAP: le ore non si aggiornano");

            if (IsBilled(current.Status, current.DestEntry))
                return new(TimesheetHoursOutcome.Billed, 409, false, current.TotalHours, current.BillableHours,
                    "La riga di timesheet è già fatturata in SAP: le ore non si aggiornano");

            if (SameHours(current.TotalHours, hours) && SameHours(current.BillableHours, billable))
                return new(TimesheetHoursOutcome.Unchanged, 200, false, current.TotalHours, current.BillableHours, null);

            var totalChanged = request.ExpectedHours.HasValue && !SameHours(current.TotalHours, request.ExpectedHours);
            var billableChanged = request.ExpectedBillableHours.HasValue && !SameHours(current.BillableHours, request.ExpectedBillableHours);
            if (totalChanged || billableChanged)
                return new(TimesheetHoursOutcome.ChangedInErp, 409, false, current.TotalHours, current.BillableHours,
                    $"La riga è stata modificata in SAP dopo il push (in SAP {Format(current.TotalHours)} h lorde / " +
                    $"{Format(current.BillableHours)} h fatturabili, attese {Format(request.ExpectedHours)} / " +
                    $"{Format(request.ExpectedBillableHours)}): non si sovrascrive");

            return new(TimesheetHoursOutcome.Updated, 200, true, hours, billable, null);
        }

        private static string Format(decimal? value)
            => value.HasValue ? value.Value.ToString("0.###", CultureInfo.InvariantCulture) : "n/d";
    }
}
