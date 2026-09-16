using AX.SAPB1.Api.Services;
using Xunit;

namespace AX.SAPB1.Api.Tests;

/// <summary>
/// La traduzione dello stato di fatturazione di una riga di timesheet (<c>@SGS_PRJ_OTMS.U_Status</c>) nel
/// contratto ERP-neutro esposto dal portale. Misurato il 16/09/2026 su dati di produzione: lo stato prende
/// esattamente tre valori — «Fatturato» (1.351 righe, sempre con <c>U_DestType = '13'</c>), «Confermato»
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
