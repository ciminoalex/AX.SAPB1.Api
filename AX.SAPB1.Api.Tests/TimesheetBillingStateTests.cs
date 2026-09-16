using AX.SAPB1.Api.Services;
using Xunit;

namespace AX.SAPB1.Api.Tests;

/// <summary>
/// La traduzione dello stato di fatturazione di una riga di timesheet (<c>@SGS_PRJ_OTMS.U_Status</c>) nel
/// contratto ERP-neutro esposto dal portale. Misurato il 16/09/2026 su dati di produzione: lo stato prende
/// esattamente tre valori — «Fatturato» (1.351 righe, di cui 1.271 con <c>U_DestType = '13'</c> verso una
/// fattura, 75 con <c>'17'</c> verso un ordine e 5 con <c>'15'</c> verso una consegna), «Confermato»
/// (5.619) e «Inserito» (49). Uno stato «Fatturato» senza <c>U_DestType</c> valorizzato non deve comunque
/// retrocedere a bozza: lo stato SAP è la fonte, il documento di destinazione è un arricchimento.
/// </summary>
public class TimesheetBillingStateTests
{
    [Theory]
    [InlineData("Fatturato", "13", "invoiced")]
    [InlineData("Fatturato", null, "invoiced")]
    [InlineData("Confermato", null, "confirmed")]
    [InlineData("Inserito", null, "draft")]
    [InlineData(null, null, "draft")]
    [InlineData("Stato sconosciuto", null, "draft")]
    public void Lo_stato_SAP_diventa_lo_stato_di_fatturazione_ERP_neutro(string? status, string? destType, string atteso)
        => Assert.Equal(atteso, DbOdbcService.MapBillingState(status, destType));
}

/// <summary>
/// La lettura di <c>@SGS_PRJ_OTMS.U_TimeNrNet</c>, che su alcune righe arriva come testo invece che come
/// numero. Il caso <c>"2.5"</c> è quello che conta: prova che il parsing usa la cultura invariante e non
/// quella del server (dove il punto è separatore delle migliaia e "2.5" diventerebbe 25 in silenzio).
/// </summary>
public class TimesheetHoursParsingTests
{
    [Theory]
    [MemberData(nameof(Casi))]
    public void Il_valore_grezzo_di_U_TimeNrNet_diventa_ore_corrette(object? raw, decimal atteso)
        => Assert.Equal(atteso, DbOdbcService.ParseHours(raw));

    public static IEnumerable<object?[]> Casi()
    {
        yield return new object?[] { null, 0m };
        yield return new object?[] { "", 0m };
        yield return new object?[] { "   ", 0m };
        yield return new object?[] { "non un numero", 0m };
        yield return new object?[] { "2.5", 2.5m };
        yield return new object?[] { "7", 7m };
        yield return new object?[] { 7.25m, 7.25m };
        yield return new object?[] { 8d, 8m };
        yield return new object?[] { 3, 3m };
    }
}

/// <summary>
/// La lettura della chiave di riserva (<c>U_ResId</c>/<c>U_Project</c>/<c>U_Activity</c>) usata per
/// l'abbinamento per attributi delle 414 righe del portale senza <c>ErpDocId</c>. Il caso che conta è
/// l'intero: su questo impianto una colonna concettualmente testuale (es. <c>U_Project</c> "48") può
/// arrivare dal driver ODBC già tipizzata numerica, e la conversione deve restare a cultura invariante
/// (mai <c>Convert.ToString</c> implicito con la cultura del server).
/// </summary>
public class TimesheetErpTextParsingTests
{
    [Theory]
    [MemberData(nameof(Casi))]
    public void Il_valore_grezzo_della_colonna_diventa_testo_ERP_neutro(object? raw, string? atteso)
        => Assert.Equal(atteso, DbOdbcService.ParseErpText(raw));

    public static IEnumerable<object?[]> Casi()
    {
        yield return new object?[] { null, null };
        yield return new object?[] { DBNull.Value, null };
        yield return new object?[] { "Dip_41_MioNoe", "Dip_41_MioNoe" };
        yield return new object?[] { "07", "07" };
        yield return new object?[] { "7", "7" };
        yield return new object?[] { 48, "48" };
        yield return new object?[] { 48m, "48" };
        // Le colonne a lunghezza fissa di SAP tornano con la coda riempita di spazi. Il portale confronta
        // questi codici per uguaglianza: uno spazio invisibile non romperebbe niente in modo rumoroso,
        // farebbe abbinare zero righe — cioè sembrerebbe che la funzione non serva.
        yield return new object?[] { "Dip_41_MioNoe   ", "Dip_41_MioNoe" };
        yield return new object?[] { "  07 ", "07" };
        // Una stringa vuota o di soli spazi è un'assenza, non un codice: vale null come il NULL SQL.
        yield return new object?[] { "", null };
        yield return new object?[] { "   ", null };
    }
}

/// <summary>
/// La lettura di <c>U_Date</c> per la stessa chiave di riserva: deve accettare sia un <see cref="DateTime"/>
/// già tipizzato dal driver sia una stringa, senza mai far cadere l'intera finestra per una riga con un
/// valore illeggibile (che diventa "data assente", non un'eccezione).
/// </summary>
public class TimesheetWorkedOnParsingTests
{
    [Theory]
    [MemberData(nameof(Casi))]
    public void Il_valore_grezzo_di_U_Date_diventa_la_data_lavorata(object? raw, DateTime? atteso)
        => Assert.Equal(atteso, DbOdbcService.ParseWorkedOn(raw));

    public static IEnumerable<object?[]> Casi()
    {
        yield return new object?[] { null, null };
        yield return new object?[] { DBNull.Value, null };
        yield return new object?[] { new DateTime(2026, 3, 4), new DateTime(2026, 3, 4) };
        yield return new object?[] { "2026-03-04", new DateTime(2026, 3, 4) };
        yield return new object?[] { "non una data", null };
    }
}
