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
