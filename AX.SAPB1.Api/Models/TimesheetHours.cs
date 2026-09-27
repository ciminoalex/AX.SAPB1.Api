namespace AX.SAPB1.Api.Models
{
    /// <summary>
    /// Body di <c>PATCH /api/timesheet/{docEntry}/hours</c>: nuove ore lorde e fatturabili di una riga di
    /// <c>@SGS_PRJ_OTMS</c> già spinta dal portale e non ancora fatturata.
    /// <para>
    /// <see cref="ExpectedHours"/> e <see cref="ExpectedBillableHours"/> sono facoltativi e abilitano il
    /// controllo di concorrenza del merge a tre vie del portale: sono la "base", cioè gli ultimi valori che il
    /// portale ha spinto. Se in SAP la riga non vale più così, qualcuno l'ha modificata a mano dopo il push e il
    /// servizio non sovrascrive (409 <c>changed_in_erp</c>). Ciascuno dei due, quando presente, si confronta
    /// da solo con la colonna corrispondente (<c>U_TimeNrTot</c> e <c>U_TimeNrNet</c>).
    /// </para>
    /// </summary>
    public class TimesheetHoursUpdateRequest
    {
        /// <summary>Ore lorde (<c>U_TimeNrTot</c>). Obbligatorio, maggiore di zero.</summary>
        public decimal? Hours { get; set; }

        /// <summary>
        /// Ore fatturabili (<c>U_TimeNrNet</c>, la quantità che SGS fattura). Obbligatorio in questo endpoint,
        /// <c>0 &lt;= BillableHours &lt;= Hours</c>. A differenza della creazione lite, qui l'assenza NON vale
        /// "uguale a Hours": in un aggiornamento un campo dimenticato riporterebbe a piene le ore che il capo
        /// progetto aveva ridotto, che è esattamente il guasto che questo endpoint esiste per correggere.
        /// </summary>
        public decimal? BillableHours { get; set; }

        /// <summary>Ore lorde che il portale si aspetta di trovare in SAP (ultimo valore spinto). Facoltativo.</summary>
        public decimal? ExpectedHours { get; set; }

        /// <summary>Ore fatturabili che il portale si aspetta di trovare in SAP (ultimo valore spinto). Facoltativo.</summary>
        public decimal? ExpectedBillableHours { get; set; }
    }

    /// <summary>
    /// Risposta di <c>PATCH /api/timesheet/{docEntry}/hours</c> (200 e 409; anche 400/404/499/502 portano lo
    /// stesso corpo). <see cref="Outcome"/> vale <c>updated</c> | <c>unchanged</c> | <c>billed</c> |
    /// <c>canceled</c> | <c>changed_in_erp</c>, più <c>invalid</c> (400), <c>not_found</c> (404) e
    /// <c>error</c> (502, rifiuto del Service Layer; 499, chiamante andato via prima della scrittura). Un 404 SENZA corpo vuol dire che la rotta non esiste,
    /// cioè servizio non ancora aggiornato: non che la riga manchi.
    /// <see cref="CurrentHours"/>/<see cref="CurrentBillableHours"/> sono i valori in SAP DOPO l'operazione
    /// (per <c>updated</c> i nuovi valori), <c>null</c> se non noti o illeggibili.
    /// </summary>
    public class TimesheetHoursUpdateResult
    {
        public string Outcome { get; set; } = string.Empty;
        public decimal? CurrentHours { get; set; }
        public decimal? CurrentBillableHours { get; set; }
        public string? Message { get; set; }
    }

    /// <summary>
    /// Stato corrente di una riga di <c>@SGS_PRJ_OTMS</c>, con tutto e solo quello che serve a decidere se le sue
    /// ore si possono aggiornare. Si legge via ODBC per la prima decisione e di nuovo dal Service Layer subito prima
    /// del PATCH. I valori sono già ripuliti (vedi <c>DbOdbcService.MapTimesheetHoursState</c> e
    /// <c>SapB1ServiceLayerService.ReadServiceLayerHoursState</c>): testo senza spazi ai bordi o <c>null</c>, ore
    /// <c>null</c> se assenti o illeggibili — MAI zero inventato, perché uno zero confrontato con le ore
    /// attese farebbe sembrare modificata a mano una riga che semplicemente non si è riusciti a leggere.
    /// </summary>
    public sealed class TimesheetHoursState
    {
        public int DocEntry { get; init; }

        /// <summary>Chiave del Service Layer (<c>SGS_PRJ_OTMS('{Code}')</c>). Diverge da DocEntry su ~12% delle righe.</summary>
        public string? Code { get; init; }

        /// <summary><c>Canceled</c>: 'Y' = riga annullata.</summary>
        public string? Canceled { get; init; }

        /// <summary><c>U_Status</c>: «Fatturato» | «Confermato» | «Inserito».</summary>
        public string? Status { get; init; }

        /// <summary><c>U_DestEntry</c>, documento di destinazione (fattura/ordine/consegna); valorizzato e diverso da 0 = riga fatturata.</summary>
        public string? DestEntry { get; init; }

        /// <summary><c>U_TimeNrTot</c>, ore lorde.</summary>
        public decimal? TotalHours { get; init; }

        /// <summary><c>U_TimeNrNet</c>, ore fatturabili.</summary>
        public decimal? BillableHours { get; init; }
    }
}
