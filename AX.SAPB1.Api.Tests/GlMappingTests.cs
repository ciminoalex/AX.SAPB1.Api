using AX.SAPB1.Api.Models;
using AX.SAPB1.Api.Services;
using Xunit;

namespace AX.SAPB1.Api.Tests;

/// <summary>
/// Le traduzioni dal gergo SAP B1 al contratto ERP-neutro della contabilità generale. Sono il punto in cui
/// un errore non si vede: un TransType mappato male sposta una fattura fra i pagamenti e nessuna query
/// fallisce. La mappatura è stata verificata incrociando TransId e documenti su questo impianto.
/// </summary>
public class GlMappingTests
{
    [Theory]
    [InlineData("13", GlSourceDocType.SalesInvoice)]
    [InlineData("18", GlSourceDocType.PurchaseInvoice)]
    [InlineData("14", GlSourceDocType.SalesCreditNote)]
    [InlineData("19", GlSourceDocType.PurchaseCreditNote)]
    [InlineData("30", GlSourceDocType.ManualJournal)]
    [InlineData("24", GlSourceDocType.Payment)]
    [InlineData("46", GlSourceDocType.Payment)]
    [InlineData("99", GlSourceDocType.Other)]
    [InlineData(null, GlSourceDocType.Other)]
    public void Il_tipo_di_registrazione_SAP_diventa_il_tipo_di_documento(string? transType, string atteso)
        => Assert.Equal(atteso, DbOdbcService.MapSourceDocType(transType));

    [Theory]
    [InlineData(4, GlStatementSection.ProductionValue)]
    [InlineData(5, GlStatementSection.ProductionCost)]
    [InlineData(6, GlStatementSection.OperatingCost)]
    [InlineData(7, GlStatementSection.OtherIncomeExpense)]
    [InlineData(8, GlStatementSection.Extraordinary)]
    public void Il_gruppo_del_conto_diventa_la_sezione_del_conto_economico(int groupMask, string atteso)
        => Assert.Equal(atteso, DbOdbcService.MapStatementSection(groupMask));

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(9)]
    public void I_conti_patrimoniali_non_hanno_una_sezione_del_conto_economico(int groupMask)
        => Assert.Null(DbOdbcService.MapStatementSection(groupMask));

    [Theory]
    [InlineData("13", "C001", "customer")]
    [InlineData("14", "C001", "customer")]
    [InlineData("18", "F001", "supplier")]
    [InlineData("19", "F001", "supplier")]
    public void Fatture_e_note_di_credito_dicono_se_la_controparte_e_cliente_o_fornitore(
        string transType, string cardCode, string atteso)
        => Assert.Equal(atteso, DbOdbcService.MapCounterpartyType(transType, cardCode));

    [Theory]
    [InlineData("13", null)]
    [InlineData("13", "  ")]
    [InlineData("30", "C001")]
    [InlineData("24", "C001")]
    public void Senza_controparte_o_fuori_da_fatture_e_note_non_c_e_un_tipo_di_controparte(
        string transType, string? cardCode)
        => Assert.Null(DbOdbcService.MapCounterpartyType(transType, cardCode));
}
