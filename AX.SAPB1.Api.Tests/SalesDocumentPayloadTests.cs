using AX.SAPB1.Api.Models;
using AX.SAPB1.Api.Services;
using AX.SAPB1.Api.Services.SalesDocuments;
using Newtonsoft.Json;
using Xunit;

namespace AX.SAPB1.Api.Tests;

/// <summary>
/// La traduzione del documento di vendita del portale nel payload del Service Layer. Qui un errore non fa
/// fallire niente in modo rumoroso: una fattura con uno sconto d'anagrafica applicato in silenzio, un
/// riferimento d'ordine perso o una scadenza forzata diventano un documento fiscale sbagliato. Le regole
/// vengono dalla specifica «Fatturazione dal portale» (§4.1) e dalle trappole del vecchio push bozza.
/// </summary>
public class SalesDocumentPayloadTests
{
    private const string CorrelationId = "0b7c5d1e-2f3a-4b5c-8d9e-0f1a2b3c4d5e";

    private static SalesDocumentRequest Richiesta(string kind = "invoice", string posting = "draft") => new()
    {
        CorrelationId = CorrelationId,
        PortalNumber = "FT-2026-0001",
        DocumentKind = kind,
        Posting = posting,
        CardCode = "IT00387",
        DocDate = new DateTime(2026, 9, 30),
        Comments = "Attività svolte dal 01-09-2026 al 30-09-2026",
        ProjectCode = "25PRJ_2500040",
        CustomerReference = new SalesDocumentCustomerReference
        {
            OrderNumber = "AD250169",
            OrderDate = new DateTime(2026, 8, 1),
            Cig = "BA1A7CF173",
        },
        Lines = new()
        {
            new SalesDocumentLineRequest
            {
                ItemCode = "ATT_MTF_CONSULENZA",
                Description = "Consulenza funzionale – E. Pasquariello – dal 01/09/2026 al 26/09/2026",
                Quantity = 4.25m,
                UnitPrice = 75m,
                UnitOfMeasureCode = "HH",
                ProjectCode = "25PRJ_2500040",
                CostingCode2 = "PAS",
                CostingCode3 = "CONS",
            },
        },
    };

    private static SalesDocumentSchema SchemaConTuttiGliUdf() => new()
    {
        HeaderUserFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "U_MTF_FE_ODA", "U_MTF_FE_DTORD", "U_MTF_FE_CIG", "U_MTF_FE_CUP", "U_AX360_InvId", "U_AX360_InvNum",
        },
    };

    private static List<Dictionary<string, object?>> Righe(Dictionary<string, object?> payload)
        => (List<Dictionary<string, object?>>)payload["DocumentLines"]!;

    // ── Destinazione ──

    [Theory]
    [InlineData("invoice", "draft", "Drafts", "oInvoices", "ODRF", "DRF1", "112")]
    [InlineData("order", "draft", "Drafts", "oOrders", "ODRF", "DRF1", "112")]
    [InlineData("invoice", "posted", "Invoices", null, "OINV", "INV1", "13")]
    [InlineData("order", "posted", "Orders", null, "ORDR", "RDR1", "17")]
    [InlineData("ORDER", " Posted ", "Orders", null, "ORDR", "RDR1", "17")]
    public void Tipo_e_stato_decidono_entita_tabelle_e_tipo_oggetto(
        string kind, string posting, string entity, string? docObjectCode, string header, string lines, string objectType)
    {
        var target = SalesDocumentPayloadBuilder.ResolveTarget(kind, posting)!;
        Assert.Equal(entity, target.Entity);
        Assert.Equal(docObjectCode, target.DocObjectCode);
        Assert.Equal(header, target.HeaderTable);
        Assert.Equal(lines, target.LineTable);
        Assert.Equal(objectType, target.ObjectType);
    }

    [Theory]
    [InlineData("credit", "draft")]
    [InlineData("invoice", "final")]
    [InlineData(null, null)]
    public void Tipo_o_stato_sconosciuti_non_hanno_destinazione(string? kind, string? posting)
        => Assert.Null(SalesDocumentPayloadBuilder.ResolveTarget(kind, posting));

    [Fact]
    public void Una_bozza_richiede_la_correlazione_anche_sulla_tabella_del_tipo()
    {
        Assert.Equal(new[] { "ORDR", "ODRF" }, SalesDocumentPayloadBuilder.RequiredCorrelationTables(SalesDocumentPayloadBuilder.ResolveTarget("order", "draft")!));
        Assert.Equal(new[] { "OINV", "ODRF" }, SalesDocumentPayloadBuilder.RequiredCorrelationTables(SalesDocumentPayloadBuilder.ResolveTarget("invoice", "draft")!));
        Assert.Equal(new[] { "ORDR" }, SalesDocumentPayloadBuilder.RequiredCorrelationTables(SalesDocumentPayloadBuilder.ResolveTarget("order", "posted")!));
    }

    // ── Validazione ──

    [Fact]
    public void Una_richiesta_completa_e_valida()
        => Assert.Empty(SalesDocumentPayloadBuilder.Validate(Richiesta()));

    [Fact]
    public void Una_riga_senza_articolo_e_rifiutata_senza_ripiego()
    {
        var r = Richiesta();
        r.Lines![0].ItemCode = "  ";
        var errors = SalesDocumentPayloadBuilder.Validate(r);
        Assert.Contains(errors, e => e.Contains("Riga 1") && e.Contains("itemCode"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Una_riga_con_quantita_non_positiva_e_rifiutata(decimal quantity)
    {
        var r = Richiesta();
        r.Lines![0].Quantity = quantity;
        Assert.Contains(SalesDocumentPayloadBuilder.Validate(r), e => e.Contains("quantity"));
    }

    [Fact]
    public void Mancano_correlazione_cliente_data_e_righe()
    {
        var errors = SalesDocumentPayloadBuilder.Validate(new SalesDocumentRequest { DocumentKind = "invoice", Posting = "draft" });
        Assert.Contains(errors, e => e.Contains("correlationId"));
        Assert.Contains(errors, e => e.Contains("cardCode"));
        Assert.Contains(errors, e => e.Contains("docDate"));
        Assert.Contains(errors, e => e.Contains("righe"));
    }

    [Fact]
    public void Un_correlationId_piu_lungo_del_campo_utente_e_rifiutato()
    {
        var r = Richiesta();
        r.CorrelationId = new string('x', 51);
        Assert.Contains(SalesDocumentPayloadBuilder.Validate(r), e => e.Contains("troppo lungo"));
    }

    // ── Testata ──

    [Fact]
    public void La_bozza_di_fattura_va_in_Drafts_con_oInvoices_e_la_correlazione()
    {
        var target = SalesDocumentPayloadBuilder.ResolveTarget("invoice", "draft")!;
        var p = SalesDocumentPayloadBuilder.Build(Richiesta(), target, SchemaConTuttiGliUdf());

        Assert.Equal("oInvoices", p["DocObjectCode"]);
        Assert.Equal("dDocument_Items", p["DocType"]);
        Assert.Equal("IT00387", p["CardCode"]);
        Assert.Equal("2026-09-30", p["DocDate"]);
        Assert.Equal("2026-09-30", p["TaxDate"]);
        Assert.Equal("25PRJ_2500040", p["Project"]);
        Assert.Equal("AD250169", p["NumAtCard"]);
        Assert.Equal(CorrelationId, p["U_AX360_InvId"]);
        Assert.Equal("FT-2026-0001", p["U_AX360_InvNum"]);
        Assert.Equal(0m, p["DiscountPercent"]);
    }

    [Fact]
    public void Ordine_definitivo_va_in_Orders_senza_DocObjectCode()
    {
        var target = SalesDocumentPayloadBuilder.ResolveTarget("order", "posted")!;
        var p = SalesDocumentPayloadBuilder.Build(Richiesta("order", "posted"), target, SchemaConTuttiGliUdf());
        Assert.Equal("Orders", target.Entity);
        Assert.False(p.ContainsKey("DocObjectCode"));
    }

    [Fact]
    public void Bozza_di_ordine_usa_oOrders()
    {
        var target = SalesDocumentPayloadBuilder.ResolveTarget("order", "draft")!;
        var p = SalesDocumentPayloadBuilder.Build(Richiesta("order", "draft"), target, SchemaConTuttiGliUdf());
        Assert.Equal("oOrders", p["DocObjectCode"]);
    }

    [Fact]
    public void Senza_scadenza_DocDueDate_non_si_invia_e_la_calcola_SAP()
    {
        var p = SalesDocumentPayloadBuilder.Build(Richiesta(), SalesDocumentPayloadBuilder.ResolveTarget("invoice", "draft")!, SchemaConTuttiGliUdf());
        Assert.False(p.ContainsKey("DocDueDate"));

        var r = Richiesta();
        r.DueDate = new DateTime(2026, 10, 30);
        var p2 = SalesDocumentPayloadBuilder.Build(r, SalesDocumentPayloadBuilder.ResolveTarget("invoice", "draft")!, SchemaConTuttiGliUdf());
        Assert.Equal("2026-10-30", p2["DocDueDate"]);
    }

    [Fact]
    public void Gli_UDF_di_fatturazione_elettronica_si_scrivono_se_esistono()
    {
        var p = SalesDocumentPayloadBuilder.Build(Richiesta(), SalesDocumentPayloadBuilder.ResolveTarget("invoice", "draft")!, SchemaConTuttiGliUdf());
        Assert.Equal("AD250169", p["U_MTF_FE_ODA"]);
        Assert.Equal("2026-08-01", p["U_MTF_FE_DTORD"]);
        Assert.Equal("BA1A7CF173", p["U_MTF_FE_CIG"]);
        Assert.False(p.ContainsKey("U_MTF_FE_CUP")); // valore assente: niente chiave
    }

    [Fact]
    public void Gli_UDF_assenti_sulla_tabella_non_si_inviano_ma_la_correlazione_si()
    {
        var schema = new SalesDocumentSchema { HeaderUserFields = new HashSet<string> { "U_AX360_InvId" } };
        var p = SalesDocumentPayloadBuilder.Build(Richiesta(), SalesDocumentPayloadBuilder.ResolveTarget("order", "posted")!, schema);
        Assert.False(p.ContainsKey("U_MTF_FE_ODA"));
        Assert.False(p.ContainsKey("U_MTF_FE_DTORD"));
        Assert.False(p.ContainsKey("U_MTF_FE_CIG"));
        Assert.False(p.ContainsKey("U_AX360_InvNum"));
        Assert.Equal(CorrelationId, p["U_AX360_InvId"]);
        // NumAtCard è un campo standard: c'è sempre, anche senza UDF FE.
        Assert.Equal("AD250169", p["NumAtCard"]);
    }

    [Fact]
    public void L_allegato_si_aggancia_con_AttachmentEntry()
    {
        var p = SalesDocumentPayloadBuilder.Build(Richiesta(), SalesDocumentPayloadBuilder.ResolveTarget("invoice", "draft")!, SchemaConTuttiGliUdf(), attachmentEntry: 7400);
        Assert.Equal(7400, p["AttachmentEntry"]);
    }

    [Fact]
    public void Commenti_e_riferimento_si_troncano_alla_lunghezza_letta_dallo_schema()
    {
        var r = Richiesta();
        r.Comments = new string('c', 300);
        r.CustomerReference!.OrderNumber = new string('o', 120);
        var schema = new SalesDocumentSchema
        {
            HeaderUserFields = new HashSet<string> { "U_AX360_InvId", "U_MTF_FE_ODA" },
            HeaderLengths = new Dictionary<string, int> { ["Comments"] = 254, ["NumAtCard"] = 100, ["U_MTF_FE_ODA"] = 250 },
        };
        var p = SalesDocumentPayloadBuilder.Build(r, SalesDocumentPayloadBuilder.ResolveTarget("invoice", "draft")!, schema);
        Assert.Equal(254, ((string)p["Comments"]!).Length);
        Assert.Equal(100, ((string)p["NumAtCard"]!).Length);
        Assert.Equal(120, ((string)p["U_MTF_FE_ODA"]!).Length);
    }

    [Fact]
    public void Senza_lunghezze_dallo_schema_valgono_quelle_di_ripiego_prudenti()
    {
        var r = Richiesta();
        r.Lines![0].Description = new string('d', 250);
        var p = SalesDocumentPayloadBuilder.Build(r, SalesDocumentPayloadBuilder.ResolveTarget("invoice", "draft")!, new SalesDocumentSchema());
        Assert.Equal(100, ((string)Righe(p)[0]["ItemDescription"]!).Length);
    }

    // ── Righe ──

    [Fact]
    public void La_riga_porta_quantita_e_prezzo_ma_mai_LineTotal()
    {
        var p = SalesDocumentPayloadBuilder.Build(Richiesta(), SalesDocumentPayloadBuilder.ResolveTarget("invoice", "draft")!, SchemaConTuttiGliUdf());
        var riga = Righe(p)[0];
        Assert.Equal("ATT_MTF_CONSULENZA", riga["ItemCode"]);
        Assert.Equal(4.25m, riga["Quantity"]);
        Assert.Equal(75m, riga["UnitPrice"]);
        Assert.Equal(0m, riga["DiscountPercent"]);
        Assert.Equal("25PRJ_2500040", riga["ProjectCode"]);
        Assert.Equal("PAS", riga["CostingCode2"]);
        Assert.Equal("CONS", riga["CostingCode3"]);
        Assert.False(riga.ContainsKey("LineTotal"));
        Assert.False(riga.ContainsKey("VatGroup"));
    }

    [Fact]
    public void Una_riga_senza_progetto_eredita_quello_di_testata()
    {
        var r = Richiesta();
        r.Lines![0].ProjectCode = null;
        var p = SalesDocumentPayloadBuilder.Build(r, SalesDocumentPayloadBuilder.ResolveTarget("invoice", "draft")!, SchemaConTuttiGliUdf());
        Assert.Equal("25PRJ_2500040", Righe(p)[0]["ProjectCode"]);
    }

    [Fact]
    public void Il_gruppo_IVA_si_invia_solo_se_indicato_sulla_riga()
    {
        var r = Richiesta();
        r.Lines![0].VatGroup = "V22";
        var p = SalesDocumentPayloadBuilder.Build(r, SalesDocumentPayloadBuilder.ResolveTarget("invoice", "draft")!, SchemaConTuttiGliUdf());
        Assert.Equal("V22", Righe(p)[0]["VatGroup"]);
    }

    [Fact]
    public void La_descrizione_di_riga_si_tronca_alla_colonna_Dscription()
    {
        var r = Richiesta();
        r.Lines![0].Description = new string('d', 250);
        var schema = new SalesDocumentSchema
        {
            HeaderUserFields = new HashSet<string> { "U_AX360_InvId" },
            LineLengths = new Dictionary<string, int> { ["Dscription"] = 200 },
        };
        var p = SalesDocumentPayloadBuilder.Build(r, SalesDocumentPayloadBuilder.ResolveTarget("invoice", "draft")!, schema);
        Assert.Equal(200, ((string)Righe(p)[0]["ItemDescription"]!).Length);
    }

    [Fact]
    public void L_unita_di_misura_va_in_UoMEntry_o_in_MeasureUnit()
    {
        var r = Richiesta();
        r.Lines!.Add(new SalesDocumentLineRequest { ItemCode = "CAN_SGS_FII", Quantity = 1, UnitPrice = 92m, UnitOfMeasureCode = "HH" });
        var uoms = new[] { new LineUom(3, null), new LineUom(null, "HH") };
        var p = SalesDocumentPayloadBuilder.Build(r, SalesDocumentPayloadBuilder.ResolveTarget("invoice", "draft")!, SchemaConTuttiGliUdf(), uoms);
        Assert.Equal(3, Righe(p)[0]["UoMEntry"]);
        Assert.False(Righe(p)[0].ContainsKey("MeasureUnit"));
        Assert.Equal("HH", Righe(p)[1]["MeasureUnit"]);
        Assert.False(Righe(p)[1].ContainsKey("UoMEntry"));
    }

    [Fact]
    public void Il_payload_serializzato_non_contiene_null()
    {
        var p = SalesDocumentPayloadBuilder.Build(Richiesta(), SalesDocumentPayloadBuilder.ResolveTarget("invoice", "draft")!, new SalesDocumentSchema());
        var json = JsonConvert.SerializeObject(p);
        Assert.DoesNotContain("null", json);
        Assert.Contains("\"Quantity\":4.25", json);
    }

    // ── Unità di misura ──

    [Fact]
    public void Articolo_con_gruppo_che_contiene_l_unita_riceve_UoMEntry()
    {
        var (uom, warning) = SalesDocumentPayloadBuilder.ResolveLineUom("ART", "HH", 3, new ItemUomInfo(2, new HashSet<int> { 1, 3 }));
        Assert.Equal(new LineUom(3, null), uom);
        Assert.Null(warning);
    }

    [Fact]
    public void Articolo_manuale_riceve_il_codice_come_testo()
    {
        var (uom, warning) = SalesDocumentPayloadBuilder.ResolveLineUom("ART", "HH", 3, new ItemUomInfo(-1, new HashSet<int>()));
        Assert.Equal(new LineUom(null, "HH"), uom);
        Assert.Null(warning);
    }

    [Fact]
    public void Unita_fuori_dal_gruppo_dell_articolo_non_si_invia_e_si_avvisa()
    {
        var (uom, warning) = SalesDocumentPayloadBuilder.ResolveLineUom("ART", "HH", 3, new ItemUomInfo(2, new HashSet<int> { 1 }));
        Assert.Equal(default, uom);
        Assert.Contains("non appartiene", warning);
    }

    [Fact]
    public void Unita_inesistente_in_OUOM_non_si_invia_e_si_avvisa()
    {
        var (uom, warning) = SalesDocumentPayloadBuilder.ResolveLineUom("ART", "XX", null, new ItemUomInfo(2, new HashSet<int> { 1 }));
        Assert.Equal(default, uom);
        Assert.Contains("inesistente", warning);
    }

    [Fact]
    public void Senza_codice_unita_non_si_invia_nulla()
    {
        var (uom, warning) = SalesDocumentPayloadBuilder.ResolveLineUom("ART", null, null, null);
        Assert.Equal(default, uom);
        Assert.Null(warning);
    }

    // ── Troncamento ──

    [Theory]
    [InlineData("abcdef", 3, "abc")]
    [InlineData("abc", 3, "abc")]
    [InlineData("abc", null, "abc")]
    [InlineData(null, 3, null)]
    public void Il_troncamento_rispetta_la_lunghezza(string? value, int? max, string? atteso)
        => Assert.Equal(atteso, SalesDocumentPayloadBuilder.Truncate(value, max));

    [Fact]
    public void Il_troncamento_non_spezza_una_coppia_surrogata()
    {
        var value = "ab\U0001F600cd"; // a, b, emoji (2 unità UTF-16), c, d
        Assert.Equal("ab", SalesDocumentPayloadBuilder.Truncate(value, 3));
    }

    // ── Risposta ──

    [Fact]
    public void Dalla_risposta_di_creazione_si_leggono_i_totali_calcolati_da_SAP()
    {
        var body = SalesDocumentPayloadBuilder.ParseBody(
            @"{""DocEntry"":123,""DocNum"":2610500,""DocTotal"":388.88,""VatSum"":70.13,""DocDueDate"":""2026-10-30T00:00:00Z"",""DocObjectCode"":""oInvoices""}");
        var result = SalesDocumentPayloadBuilder.ParseCreated(body, SalesDocumentPayloadBuilder.ResolveTarget("invoice", "draft")!)!;

        Assert.True(result.Success);
        Assert.Equal("invoice", result.DocumentKind);
        Assert.Equal("draft", result.Status);
        Assert.Equal("112", result.ObjectType);
        Assert.Equal(123, result.DocEntry);
        Assert.Equal(2610500, result.DocNum);
        Assert.Equal(388.88m, result.DocTotal);
        Assert.Equal(70.13m, result.VatSum);
        Assert.Equal(new DateTime(2026, 10, 30), result.DocDueDate);
        Assert.False(result.AlreadyExisted);
    }

    [Theory]
    [InlineData(@"""2026-10-30""")]
    [InlineData(@"""2026-10-30T00:00:00""")]
    [InlineData(@"""2026-10-30T00:00:00+02:00""")]
    public void La_scadenza_non_slitta_di_un_giorno_per_il_fuso(string raw)
    {
        var body = SalesDocumentPayloadBuilder.ParseBody($@"{{""DocEntry"":1,""DocDueDate"":{raw}}}");
        var result = SalesDocumentPayloadBuilder.ParseCreated(body, SalesDocumentPayloadBuilder.ResolveTarget("order", "posted")!)!;
        Assert.Equal(new DateTime(2026, 10, 30), result.DocDueDate);
    }

    [Fact]
    public void Senza_DocEntry_la_risposta_non_e_un_documento()
        => Assert.Null(SalesDocumentPayloadBuilder.ParseCreated(SalesDocumentPayloadBuilder.ParseBody("{}"), SalesDocumentPayloadBuilder.ResolveTarget("order", "posted")!));

    [Theory]
    [InlineData("invoice", "posted", "13")]
    [InlineData("order", "posted", "17")]
    [InlineData("invoice", "draft", "112")]
    [InlineData("order", "draft", "112")]
    public void Il_documento_esistente_torna_con_il_suo_tipo_oggetto(string kind, string status, string objectType)
    {
        var result = SalesDocumentPayloadBuilder.FromExisting(new ExistingSalesDocument { DocumentKind = kind, Status = status, DocEntry = 5, DocNum = 9 });
        Assert.True(result.AlreadyExisted);
        Assert.Equal(objectType, result.ObjectType);
        Assert.Equal(kind, result.DocumentKind);
        Assert.Equal(status, result.Status);
    }

    [Fact]
    public void Fra_piu_documenti_vince_il_definitivo_valido()
    {
        var scelta = SalesDocumentPayloadBuilder.SelectExisting(new[]
        {
            new ExistingSalesDocument { Table = "ODRF", DocumentKind = "invoice", Status = "draft", DocEntry = 1, Canceled = "N" },
            new ExistingSalesDocument { Table = "OINV", DocumentKind = "invoice", Status = "posted", DocEntry = 90, Canceled = "Y" },
            new ExistingSalesDocument { Table = "OINV", DocumentKind = "invoice", Status = "posted", DocEntry = 50, Canceled = "N" },
        })!;
        Assert.Equal(50, scelta.DocEntry);
    }

    [Fact]
    public void Un_nuovo_push_dell_ordine_ritrova_l_ordine_e_non_la_fattura_che_ne_ha_ereditato_l_UDF()
    {
        var candidati = new[]
        {
            new ExistingSalesDocument { Table = "OINV", DocumentKind = "invoice", Status = "posted", DocEntry = 10, Canceled = "N" },
            new ExistingSalesDocument { Table = "ORDR", DocumentKind = "order", Status = "posted", DocEntry = 300, Canceled = "N" },
        };
        Assert.Equal("order", SalesDocumentPayloadBuilder.SelectExisting(candidati, "order")!.DocumentKind);
        Assert.Equal("invoice", SalesDocumentPayloadBuilder.SelectExisting(candidati, "invoice")!.DocumentKind);
    }

    [Fact]
    public void Un_documento_annullato_conta_come_esistente()
    {
        var scelta = SalesDocumentPayloadBuilder.SelectExisting(new[]
        {
            new ExistingSalesDocument { Table = "ORDR", DocumentKind = "order", Status = "posted", DocEntry = 7, Canceled = "Y" },
        });
        Assert.NotNull(scelta);
    }

    [Fact]
    public void Nessun_candidato_nessun_documento()
        => Assert.Null(SalesDocumentPayloadBuilder.SelectExisting(Array.Empty<ExistingSalesDocument>()));

    // ── Allegati ──

    [Theory]
    [InlineData("Comal - Attivita settembre 2026.pdf", "Comal - Attivita settembre 2026_0b7c5d1e.pdf")]
    [InlineData("Comal - Attività settembre 2026.pdf", "Comal - Attivita settembre 2026_0b7c5d1e.pdf")]
    [InlineData(@"C:\temp\rapporto.pdf", "rapporto_0b7c5d1e.pdf")]
    [InlineData("rapporto", "rapporto_0b7c5d1e.pdf")]
    [InlineData("\"strano\"/nome?.pdf", "nome_0b7c5d1e.pdf")]
    [InlineData("", "allegato_0b7c5d1e.pdf")]
    public void Il_nome_dell_allegato_e_ASCII_senza_percorso_e_con_suffisso_univoco(string input, string atteso)
        => Assert.Equal(atteso, SalesDocumentPayloadBuilder.SanitizeAttachmentFileName(input, CorrelationId));

    // ── Errori del Service Layer ──

    [Fact]
    public void Il_messaggio_di_errore_SAP_si_estrae_dal_corpo()
    {
        const string body = @"{""error"":{""code"":-10,""message"":{""lang"":""en-us"",""value"":""Invalid item code""}}}";
        Assert.Equal("Invalid item code", ServiceLayerErrors.ExtractMessage(body));
        Assert.False(ServiceLayerErrors.IsAlreadyExists(body));
    }

    [Theory]
    [InlineData(@"{""error"":{""code"":-2035,""message"":{""lang"":""en-us"",""value"":""This entry already exists in the following tables""}}}")]
    [InlineData(@"{""error"":{""code"":-1,""message"":""This entry already exists (ODBC -2035)""}}")]
    public void La_chiave_gia_esistente_si_riconosce(string body)
        => Assert.True(ServiceLayerErrors.IsAlreadyExists(body));

    [Fact]
    public void Un_corpo_non_JSON_torna_come_testo()
        => Assert.Equal("Bad Gateway", ServiceLayerErrors.ExtractMessage("Bad Gateway"));
}

/// <summary>
/// Stato degli ordini cliente e attività degli articoli: le traduzioni SAP → contratto neutro degli
/// endpoint <c>/api/sales-orders</c> e <c>/api/lookup/items</c>.
/// </summary>
public class SalesOrderAndItemMappingTests
{
    [Theory]
    [InlineData("O", "N", "open")]
    [InlineData("C", "N", "closed")]
    [InlineData("C", "Y", "cancelled")]
    [InlineData("O", "C", "cancelled")]
    [InlineData(null, null, "open")]
    public void Lo_stato_dell_ordine_diventa_open_closed_o_cancelled(string? docStatus, string? canceled, string atteso)
        => Assert.Equal(atteso, DbOdbcService.MapSalesOrderStatus(docStatus, canceled));

    private static readonly DateTime Oggi = new(2026, 9, 27);

    [Fact]
    public void Articolo_senza_restrizioni_e_attivo()
        => Assert.True(DbOdbcService.IsItemActive("N", null, null, "N", null, null, Oggi));

    [Fact]
    public void Articolo_bloccato_senza_date_e_inattivo()
        => Assert.False(DbOdbcService.IsItemActive("N", null, null, "Y", null, null, Oggi));

    [Fact]
    public void Articolo_bloccato_solo_in_futuro_e_ancora_attivo()
        => Assert.True(DbOdbcService.IsItemActive("N", null, null, "Y", new DateTime(2026, 10, 1), null, Oggi));

    [Fact]
    public void Articolo_bloccato_in_un_periodo_gia_finito_e_attivo()
        => Assert.True(DbOdbcService.IsItemActive("N", null, null, "Y", new DateTime(2026, 1, 1), new DateTime(2026, 6, 30), Oggi));

    [Fact]
    public void Articolo_valido_solo_in_un_intervallo_che_contiene_oggi_e_attivo()
        => Assert.True(DbOdbcService.IsItemActive("Y", new DateTime(2026, 1, 1), new DateTime(2026, 12, 31), "N", null, null, Oggi));

    [Fact]
    public void Articolo_valido_solo_fino_a_ieri_e_inattivo()
        => Assert.False(DbOdbcService.IsItemActive("Y", null, new DateTime(2026, 9, 26), "N", null, null, Oggi));

    [Fact]
    public void Articolo_valido_senza_date_e_attivo()
        => Assert.True(DbOdbcService.IsItemActive("Y", null, null, "N", null, null, Oggi));
}
