using AX.SAPB1.Api.Controllers;
using AX.SAPB1.Api.Models;
using AX.SAPB1.Api.Services;
using AX.SAPB1.Api.Services.SalesDocuments;
using AX.SAPB1.Api.Services.Timesheets;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AX.SAPB1.Api.Tests;

/// <summary>
/// Creazione lite con ore fatturabili. SGS fattura il T&amp;M da <c>U_TimeNrNet</c>: finché il servizio scriveva
/// lì le ore lorde, una riduzione delle ore fatturabili fatta nel portale non arrivava mai in fattura (caso reale:
/// Comal 22-23/09/2026, 6,5 h lorde e 4 fatturabili, fatturate 6,5).
/// </summary>
public class TimesheetLitePayloadTests
{
    private static readonly ProjectLookupDetail Project = new() { Code = "48", Name = "Comal AMS", CardCode = "IT00387", CardName = "Comal" };
    private static readonly ActivitySummary Activity = new() { Code = "07", Name = "Consulenza" };

    private static TimesheetCreateRequestLite Request(decimal? hours, decimal? billable) => new()
    {
        Date = new DateTime(2026, 9, 22),
        ResId = "Dip_41_MioNoe",
        Project = "48",
        ActivityId = "07",
        Hours = hours,
        Desc = "Analisi",
        BillableHours = billable,
    };

    [Fact]
    public void Con_ore_fatturabili_ridotte_Net_porta_le_fatturabili_e_NF_la_differenza()
    {
        var payload = SapB1ServiceLayerService.BuildLitePayload(Request(6.5m, 4m), Project, Activity);

        Assert.Equal(6.5m, payload.U_TimeNrTot);
        Assert.Equal(4m, payload.U_TimeNrNet);
        Assert.Equal(2.5m, payload.U_TimeNrNF);
        // Il resto resta com'era: tempo lavorato sulle ore lorde, pausa e campi *Ori a zero.
        Assert.Equal("09:00:00", payload.U_TimeStart);
        Assert.Equal("15:30:00", payload.U_TimeEnd);
        Assert.Equal(0m, payload.U_TimeNrPa);
        Assert.Equal(0m, payload.U_TimeNrPaOri);
        Assert.Equal(0m, payload.U_TimeNrNFOri);
        Assert.Equal(0m, payload.U_TimeNrTotOri);
        Assert.Equal(0m, payload.U_TimeNrNetOri);
        Assert.Equal("Confermato", payload.U_Status);
    }

    [Fact]
    public void Senza_ore_fatturabili_il_payload_resta_quello_di_prima()
    {
        var payload = SapB1ServiceLayerService.BuildLitePayload(Request(8m, null), Project, Activity);

        Assert.Equal(8m, payload.U_TimeNrTot);
        Assert.Equal(8m, payload.U_TimeNrNet);
        Assert.Equal(0m, payload.U_TimeNrNF);
    }

    [Fact]
    public void Zero_ore_fatturabili_e_ammesso_e_mette_tutto_in_NF()
    {
        Assert.Null(TimesheetHoursRules.ValidateBillableHours(3m, 0m));
        var payload = SapB1ServiceLayerService.BuildLitePayload(Request(3m, 0m), Project, Activity);

        Assert.Equal(3m, payload.U_TimeNrTot);
        Assert.Equal(0m, payload.U_TimeNrNet);
        Assert.Equal(3m, payload.U_TimeNrNF);
    }

    [Fact]
    public void Ore_fatturabili_uguali_alle_lorde_sono_ammesse()
    {
        Assert.Null(TimesheetHoursRules.ValidateBillableHours(4m, 4m));
        Assert.Null(TimesheetHoursRules.ValidateBillableHours(4m, null));
    }

    [Theory]
    [InlineData(-0.5)]
    [InlineData(6.51)]
    [InlineData(10)]
    public void Ore_fatturabili_fuori_intervallo_sono_rifiutate_e_mai_scritte(double billable)
    {
        var value = (decimal)billable;

        var error = TimesheetHoursRules.ValidateBillableHours(6.5m, value);
        Assert.NotNull(error);
        Assert.Contains("BillableHours", error);

        // Ultima difesa: anche se un chiamante saltasse la validazione, in SAP non va una riga incoerente.
        Assert.Throws<ArgumentOutOfRangeException>(() => SapB1ServiceLayerService.BuildLitePayload(Request(6.5m, value), Project, Activity));
    }

    [Fact]
    public async Task Il_controller_rifiuta_con_400_le_ore_fatturabili_fuori_intervallo_prima_di_toccare_SAP()
    {
        // Nessun gestore: se il controller interrogasse ODBC o il Service Layer, il finto lancerebbe e la risposta
        // sarebbe 500, non 400.
        var controller = new TimesheetController(
            InterfaceFake<IDbOdbcService>.Create(new()),
            InterfaceFake<ISapB1ServiceLayerService>.Create(new()),
            NullLogger<TimesheetController>.Instance);

        var created = await controller.CreateTimesheetLite(Request(6.5m, 7m));
        var preview = await controller.PreviewTimesheetLiteMapping(Request(6.5m, -1m));

        Assert.Equal(400, Assert.IsAssignableFrom<ObjectResult>(created.Result).StatusCode);
        Assert.Equal(400, Assert.IsAssignableFrom<ObjectResult>(preview.Result).StatusCode);
    }
}

/// <summary>
/// La decisione di <c>PATCH /api/timesheet/{docEntry}/hours</c>, ramo per ramo, nell'ordine del contratto. Il
/// principio: una riga fatturata o annullata non si tocca mai, una riga modificata a mano in SAP dopo il push non si
/// sovrascrive, e un nuovo tentativo dopo un timeout riuscito non scrive due volte.
/// </summary>
public class TimesheetHoursDecisionTests
{
    private static TimesheetHoursState Row(
        decimal? total = 6.5m, decimal? net = 6.5m, string? status = "Confermato", string? destEntry = null, string? canceled = "N")
        => new() { DocEntry = 7128, Code = "7121", Canceled = canceled, Status = status, DestEntry = destEntry, TotalHours = total, BillableHours = net };

    private static TimesheetHoursUpdateRequest Req(decimal? hours = 6.5m, decimal? billable = 4m, decimal? expHours = null, decimal? expBillable = null)
        => new() { Hours = hours, BillableHours = billable, ExpectedHours = expHours, ExpectedBillableHours = expBillable };

    // ── Validazione (400) ──

    [Theory]
    [MemberData(nameof(RichiesteNonValide))]
    public void Richiesta_non_valida_da_400_senza_guardare_la_riga(TimesheetHoursUpdateRequest? request)
    {
        var decision = TimesheetHoursRules.Decide(Row(), request);

        Assert.Equal(TimesheetHoursOutcome.Invalid, decision.Outcome);
        Assert.Equal(400, decision.HttpStatus);
        Assert.False(decision.ShouldWrite);
        Assert.False(string.IsNullOrWhiteSpace(decision.Message));
    }

    public static IEnumerable<object?[]> RichiesteNonValide()
    {
        yield return new object?[] { null };
        yield return new object?[] { Req(hours: null) };
        yield return new object?[] { Req(hours: 0m, billable: 0m) };
        yield return new object?[] { Req(hours: -1m, billable: 0m) };
        // In aggiornamento le ore fatturabili sono obbligatorie: l'assenza non vale "uguale alle lorde".
        yield return new object?[] { Req(billable: null) };
        yield return new object?[] { Req(billable: -0.25m) };
        yield return new object?[] { Req(hours: 4m, billable: 4.5m) };
    }

    [Fact]
    public void La_validazione_viene_prima_anche_della_riga_inesistente()
        => Assert.Equal(400, TimesheetHoursRules.Decide(null, Req(hours: 0m)).HttpStatus);

    // ── Riga inesistente (404) ──

    [Fact]
    public void Riga_inesistente_da_404()
    {
        var decision = TimesheetHoursRules.Decide(null, Req());

        Assert.Equal(TimesheetHoursOutcome.NotFound, decision.Outcome);
        Assert.Equal(404, decision.HttpStatus);
        Assert.False(decision.ShouldWrite);
    }

    // ── Riga annullata (409 canceled) ──

    [Theory]
    [InlineData("Y")]
    [InlineData("y")]
    [InlineData(" Y ")]
    [InlineData("tYES")]
    public void Riga_annullata_da_409_canceled(string canceled)
    {
        var decision = TimesheetHoursRules.Decide(Row(canceled: canceled), Req());

        Assert.Equal(TimesheetHoursOutcome.Canceled, decision.Outcome);
        Assert.Equal(409, decision.HttpStatus);
        Assert.False(decision.ShouldWrite);
        Assert.Equal(6.5m, decision.CurrentHours);
        Assert.Equal(6.5m, decision.CurrentBillableHours);
    }

    [Fact]
    public void Annullata_e_fatturata_risponde_canceled_come_da_ordine_del_contratto()
        => Assert.Equal(TimesheetHoursOutcome.Canceled,
            TimesheetHoursRules.Decide(Row(canceled: "Y", status: "Fatturato", destEntry: "3301"), Req()).Outcome);

    // ── Riga fatturata (409 billed): per stato O per documento di destinazione ──

    [Theory]
    [InlineData("Fatturato", null)]
    [InlineData("fatturato ", null)]
    [InlineData("Confermato", "3301")]
    [InlineData("Inserito", "12")]
    [InlineData(null, "ABC")]
    [InlineData("Fatturato", "3301")]
    [InlineData("Confermato", "0,0")]
    public void Riga_fatturata_da_409_billed_e_non_si_scrive(string? status, string? destEntry)
    {
        var decision = TimesheetHoursRules.Decide(Row(status: status, destEntry: destEntry), Req());

        Assert.Equal(TimesheetHoursOutcome.Billed, decision.Outcome);
        Assert.Equal(409, decision.HttpStatus);
        Assert.False(decision.ShouldWrite);
        Assert.Equal(6.5m, decision.CurrentHours);
    }

    [Fact]
    public void Riga_fatturata_non_si_tocca_nemmeno_se_ha_gia_i_valori_richiesti()
        => Assert.Equal(TimesheetHoursOutcome.Billed,
            TimesheetHoursRules.Decide(Row(total: 6.5m, net: 4m, status: "Fatturato"), Req(6.5m, 4m)).Outcome);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData(" 0 ")]
    [InlineData("0.0")]
    public void Documento_di_destinazione_vuoto_o_zero_non_e_fatturazione(string? destEntry)
    {
        Assert.False(TimesheetHoursRules.HasDestinationDocument(destEntry));
        Assert.Equal(TimesheetHoursOutcome.Updated, TimesheetHoursRules.Decide(Row(destEntry: destEntry), Req()).Outcome);
    }

    // ── Già allineata (200 unchanged) ──

    [Fact]
    public void Valori_gia_in_SAP_danno_unchanged_senza_scrivere()
    {
        var decision = TimesheetHoursRules.Decide(Row(total: 6.5m, net: 4m), Req(6.5m, 4m));

        Assert.Equal(TimesheetHoursOutcome.Unchanged, decision.Outcome);
        Assert.Equal(200, decision.HttpStatus);
        Assert.False(decision.ShouldWrite);
        Assert.Equal(6.5m, decision.CurrentHours);
        Assert.Equal(4m, decision.CurrentBillableHours);
    }

    [Fact]
    public void Unchanged_rispetta_la_tolleranza_di_un_millesimo()
    {
        Assert.Equal(TimesheetHoursOutcome.Unchanged, TimesheetHoursRules.Decide(Row(total: 6.5005m, net: 3.999m), Req(6.5m, 4m)).Outcome);
        Assert.Equal(TimesheetHoursOutcome.Updated, TimesheetHoursRules.Decide(Row(total: 6.5m, net: 4.01m), Req(6.5m, 4m)).Outcome);
    }

    [Fact]
    public void Nuovo_tentativo_dopo_timeout_riuscito_da_unchanged_e_non_changed_in_erp()
    {
        // Il primo PATCH ha scritto 6,5/4 ma la risposta si è persa: il portale ritenta con gli stessi attesi di
        // prima (6,5/6,5), che in SAP non ci sono più. Deve vincere "già fatto", non "modificata da altri".
        var decision = TimesheetHoursRules.Decide(Row(total: 6.5m, net: 4m), Req(6.5m, 4m, expHours: 6.5m, expBillable: 6.5m));

        Assert.Equal(TimesheetHoursOutcome.Unchanged, decision.Outcome);
        Assert.Equal(200, decision.HttpStatus);
    }

    // ── Modificata a mano in SAP (409 changed_in_erp) ──

    [Fact]
    public void Ore_fatturabili_cambiate_in_SAP_danno_changed_in_erp()
    {
        var decision = TimesheetHoursRules.Decide(Row(total: 6.5m, net: 5m), Req(6.5m, 4m, expHours: 6.5m, expBillable: 6.5m));

        Assert.Equal(TimesheetHoursOutcome.ChangedInErp, decision.Outcome);
        Assert.Equal(409, decision.HttpStatus);
        Assert.False(decision.ShouldWrite);
        Assert.Equal(6.5m, decision.CurrentHours);
        Assert.Equal(5m, decision.CurrentBillableHours);
        Assert.Contains("modificata in SAP", decision.Message);
    }

    [Fact]
    public void Ore_lorde_cambiate_in_SAP_danno_changed_in_erp()
        => Assert.Equal(TimesheetHoursOutcome.ChangedInErp,
            TimesheetHoursRules.Decide(Row(total: 7m, net: 6.5m), Req(6.5m, 4m, expHours: 6.5m, expBillable: 6.5m)).Outcome);

    [Fact]
    public void Basta_uno_solo_degli_attesi_per_accorgersi_della_modifica()
    {
        Assert.Equal(TimesheetHoursOutcome.ChangedInErp,
            TimesheetHoursRules.Decide(Row(total: 7m, net: 6.5m), Req(6.5m, 4m, expHours: 6.5m)).Outcome);
        Assert.Equal(TimesheetHoursOutcome.ChangedInErp,
            TimesheetHoursRules.Decide(Row(total: 6.5m, net: 5m), Req(6.5m, 4m, expBillable: 6.5m)).Outcome);
    }

    [Fact]
    public void Ore_illeggibili_in_SAP_con_attesi_presenti_non_si_sovrascrivono()
        => Assert.Equal(TimesheetHoursOutcome.ChangedInErp,
            TimesheetHoursRules.Decide(Row(total: null, net: null), Req(6.5m, 4m, expHours: 6.5m, expBillable: 6.5m)).Outcome);

    [Fact]
    public void Attesi_uguali_entro_la_tolleranza_non_sono_una_modifica()
        => Assert.Equal(TimesheetHoursOutcome.Updated,
            TimesheetHoursRules.Decide(Row(total: 6.5004m, net: 6.4996m), Req(6.5m, 4m, expHours: 6.5m, expBillable: 6.5m)).Outcome);

    // ── Aggiornamento (200 updated) ──

    [Fact]
    public void Attesi_coincidenti_con_SAP_danno_updated_con_i_nuovi_valori()
    {
        var decision = TimesheetHoursRules.Decide(Row(total: 6.5m, net: 6.5m), Req(6.5m, 4m, expHours: 6.5m, expBillable: 6.5m));

        Assert.Equal(TimesheetHoursOutcome.Updated, decision.Outcome);
        Assert.Equal(200, decision.HttpStatus);
        Assert.True(decision.ShouldWrite);
        Assert.Equal(6.5m, decision.CurrentHours);
        Assert.Equal(4m, decision.CurrentBillableHours);
        Assert.Null(decision.Message);
    }

    [Fact]
    public void Senza_attesi_si_aggiorna_anche_se_SAP_ha_valori_diversi_da_quelli_spinti()
    {
        var decision = TimesheetHoursRules.Decide(Row(total: 8m, net: 7m), Req(6.5m, 4m));

        Assert.Equal(TimesheetHoursOutcome.Updated, decision.Outcome);
        Assert.True(decision.ShouldWrite);
    }

    [Fact]
    public void Si_aggiornano_anche_le_sole_ore_lorde()
    {
        var decision = TimesheetHoursRules.Decide(Row(total: 6.5m, net: 4m), Req(7m, 4m, expHours: 6.5m, expBillable: 4m));

        Assert.Equal(TimesheetHoursOutcome.Updated, decision.Outcome);
        Assert.Equal(7m, decision.CurrentHours);
        Assert.Equal(4m, decision.CurrentBillableHours);
    }

    [Fact]
    public void La_ripartizione_scritta_porta_la_differenza_in_NF()
    {
        Assert.Equal((6.5m, 4m, 2.5m), TimesheetHoursRules.SplitHours(6.5m, 4m));
        Assert.Equal((2m, 0m, 2m), TimesheetHoursRules.SplitHours(2m, 0m));
        Assert.Equal((2m, 2m, 0m), TimesheetHoursRules.SplitHours(2m, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => TimesheetHoursRules.SplitHours(0m, 0m));
        Assert.Throws<ArgumentOutOfRangeException>(() => TimesheetHoursRules.SplitHours(2m, 2.5m));
    }
}

/// <summary>
/// Il flusso del controller attorno alla decisione: cosa legge, se scrive, e cosa risponde. I finti lanciano su
/// ogni chiamata non prevista, quindi "il Service Layer non è stato chiamato" è verificato, non supposto.
/// </summary>
public class TimesheetHoursEndpointTests
{
    private static TimesheetController Controller(TimesheetHoursState? current, Func<object?[], object?>? write = null, Action? onRead = null)
    {
        var db = InterfaceFake<IDbOdbcService>.Create(new()
        {
            ["GetTimesheetHoursStateAsync"] = _ => { onRead?.Invoke(); return Task.FromResult(current); },
        });
        var handlers = new Dictionary<string, Func<object?[], object?>>();
        if (write is not null) handlers["UpdateTimesheetHoursAsync"] = write;
        return new TimesheetController(db, InterfaceFake<ISapB1ServiceLayerService>.Create(handlers), NullLogger<TimesheetController>.Instance);
    }

    private static Task<TimesheetHoursWriteResult> Written(int status, bool success, string body, TimesheetHoursState? fresh)
        => Task.FromResult(TimesheetHoursWriteResult.FromResponse(new ServiceLayerResponse(status, success, body), fresh));

    private static async Task<(int Status, TimesheetHoursUpdateResult Body)> CallAsync(TimesheetController controller, int docEntry, TimesheetHoursUpdateRequest request)
    {
        var result = await controller.UpdateTimesheetHours(docEntry, request);
        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result.Result);
        return (objectResult.StatusCode ?? 0, Assert.IsType<TimesheetHoursUpdateResult>(objectResult.Value));
    }

    private static TimesheetHoursState Row(decimal total, decimal net, string status = "Confermato", string? destEntry = null)
        => new() { DocEntry = 7128, Code = "7121", Canceled = "N", Status = status, DestEntry = destEntry, TotalHours = total, BillableHours = net };

    [Fact]
    public async Task Aggiorna_con_le_ore_richieste_e_risponde_updated()
    {
        (int DocEntry, decimal Hours, decimal Billable)? scritto = null;
        object? statoPassato = null;
        var letta = Row(6.5m, 6.5m);
        var controller = Controller(letta, args =>
        {
            var richiesta = (TimesheetHoursUpdateRequest)args[1]!;
            scritto = ((int)args[0]!, richiesta.Hours!.Value, richiesta.BillableHours!.Value);
            statoPassato = args[2];
            return Written(204, true, string.Empty, letta);
        });

        var (status, body) = await CallAsync(controller, 7128, new() { Hours = 6.5m, BillableHours = 4m, ExpectedHours = 6.5m, ExpectedBillableHours = 6.5m });

        Assert.Equal(200, status);
        Assert.Equal("updated", body.Outcome);
        Assert.Equal(6.5m, body.CurrentHours);
        Assert.Equal(4m, body.CurrentBillableHours);
        Assert.Equal((7128, 6.5m, 4m), scritto);
        // Lo stato ODBC arriva al Service Layer come riserva per le colonne che la sua risposta non espone.
        Assert.Same(letta, statoPassato);
    }

    [Fact]
    public async Task Riga_fatturata_risponde_409_billed_senza_chiamare_il_Service_Layer()
    {
        var (status, body) = await CallAsync(Controller(Row(6.5m, 6.5m, "Fatturato", "3301")), 7128, new() { Hours = 6.5m, BillableHours = 4m });

        Assert.Equal(409, status);
        Assert.Equal("billed", body.Outcome);
        Assert.Equal(6.5m, body.CurrentBillableHours);
    }

    [Fact]
    public async Task Riga_gia_allineata_risponde_200_unchanged_senza_scrivere()
    {
        var (status, body) = await CallAsync(Controller(Row(6.5m, 4m)), 7128, new() { Hours = 6.5m, BillableHours = 4m, ExpectedHours = 6.5m, ExpectedBillableHours = 6.5m });

        Assert.Equal(200, status);
        Assert.Equal("unchanged", body.Outcome);
    }

    [Fact]
    public async Task Riga_modificata_in_SAP_risponde_409_changed_in_erp_senza_scrivere()
    {
        var (status, body) = await CallAsync(Controller(Row(6.5m, 5m)), 7128, new() { Hours = 6.5m, BillableHours = 4m, ExpectedHours = 6.5m, ExpectedBillableHours = 6.5m });

        Assert.Equal(409, status);
        Assert.Equal("changed_in_erp", body.Outcome);
        Assert.Equal(5m, body.CurrentBillableHours);
    }

    [Fact]
    public async Task Riga_inesistente_risponde_404_con_corpo_not_found()
    {
        var (status, body) = await CallAsync(Controller(null), 99999, new() { Hours = 6.5m, BillableHours = 4m });

        Assert.Equal(404, status);
        Assert.Equal("not_found", body.Outcome);
    }

    [Fact]
    public async Task Richiesta_non_valida_risponde_400_senza_leggere_SAP()
    {
        var controller = new TimesheetController(
            InterfaceFake<IDbOdbcService>.Create(new()),
            InterfaceFake<ISapB1ServiceLayerService>.Create(new()),
            NullLogger<TimesheetController>.Instance);

        var (status, body) = await CallAsync(controller, 7128, new() { Hours = 4m, BillableHours = 5m });

        Assert.Equal(400, status);
        Assert.Equal("invalid", body.Outcome);
    }

    [Fact]
    public async Task Rifiuto_del_Service_Layer_risponde_502_con_i_valori_ancora_in_SAP()
    {
        var controller = Controller(Row(6.5m, 6.5m), _ => Written(400, false,
            "{\"error\":{\"code\":-1,\"message\":{\"lang\":\"en-us\",\"value\":\"Field U_TimeNrNet invalid\"}}}", Row(6.5m, 6.5m)));

        var (status, body) = await CallAsync(controller, 7128, new() { Hours = 6.5m, BillableHours = 4m });

        Assert.Equal(502, status);
        Assert.Equal("error", body.Outcome);
        Assert.Equal(6.5m, body.CurrentBillableHours);
        Assert.Contains("Field U_TimeNrNet invalid", body.Message);
    }

    [Fact]
    public async Task Riga_fatturata_fra_la_lettura_e_la_scrittura_risponde_409_billed_con_i_valori_riletti()
    {
        // ODBC la vede ancora confermata; il Service Layer, riletta subito prima del PATCH, la trova fatturata e non
        // scrive. Al portale arriva il rifiuto, non un "updated" su una fattura appena emessa.
        var riletta = Row(6.5m, 6.5m, "Fatturato", "3301");
        var controller = Controller(Row(6.5m, 6.5m), args =>
        {
            var decisione = TimesheetHoursRules.Decide(riletta, (TimesheetHoursUpdateRequest)args[1]!);
            return Task.FromResult(TimesheetHoursWriteResult.NotWritten(decisione, riletta));
        });

        var (status, body) = await CallAsync(controller, 7128, new() { Hours = 6.5m, BillableHours = 4m, ExpectedHours = 6.5m, ExpectedBillableHours = 6.5m });

        Assert.Equal(409, status);
        Assert.Equal("billed", body.Outcome);
        Assert.Equal(6.5m, body.CurrentBillableHours);
    }

    [Fact]
    public async Task Chiamante_andato_via_dopo_la_lettura_non_arriva_al_Service_Layer()
    {
        // Il portale va in timeout mentre il servizio legge: il Service Layer non va nemmeno chiamato (il finto,
        // senza gestori, lancerebbe e la risposta sarebbe 500).
        using var abbandono = new CancellationTokenSource();
        var controller = Controller(Row(6.5m, 6.5m), onRead: abbandono.Cancel);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { RequestAborted = abbandono.Token } };

        var (status, body) = await CallAsync(controller, 7128, new() { Hours = 6.5m, BillableHours = 4m });

        Assert.Equal(StatusCodes.Status499ClientClosedRequest, status);
        Assert.Equal("error", body.Outcome);
    }

    [Fact]
    public async Task Abbandono_segnalato_dal_Service_Layer_prima_del_PATCH_risponde_499()
    {
        using var abbandono = new CancellationTokenSource();
        CancellationToken ricevuto = default;
        var controller = Controller(Row(6.5m, 6.5m), args =>
        {
            ricevuto = (CancellationToken)args[3]!;
            abbandono.Cancel();
            ricevuto.ThrowIfCancellationRequested();
            return Written(204, true, string.Empty, null);
        });
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { RequestAborted = abbandono.Token } };

        var (status, _) = await CallAsync(controller, 7128, new() { Hours = 6.5m, BillableHours = 4m });

        Assert.Equal(StatusCodes.Status499ClientClosedRequest, status);
        Assert.Equal(abbandono.Token, ricevuto);
    }
}

/// <summary>
/// La lettura dello stato ore di una riga dai valori grezzi del driver ODBC. Su questo impianto
/// <c>U_TimeNrTot</c>/<c>U_TimeNrNet</c> possono arrivare come testo e <c>U_DestEntry</c> è alfanumerico: il
/// testo si legge a cultura invariante, e un valore assente o illeggibile resta <c>null</c> — mai uno zero
/// inventato, che nel controllo di concorrenza sembrerebbe una modifica fatta a mano in SAP.
/// </summary>
public class TimesheetHoursStateParsingTests
{
    [Theory]
    [MemberData(nameof(Ore))]
    public void Il_valore_grezzo_delle_ore_diventa_ore_o_null(object? raw, decimal? atteso)
        => Assert.Equal(atteso, DbOdbcService.ParseNullableHours(raw));

    public static IEnumerable<object?[]> Ore()
    {
        yield return new object?[] { null, null };
        yield return new object?[] { DBNull.Value, null };
        yield return new object?[] { "", null };
        yield return new object?[] { "   ", null };
        yield return new object?[] { "non un numero", null };
        yield return new object?[] { "2.5", 2.5m };
        // La virgola non è un separatore ammesso: con le migliaia attive «6,5» diventerebbe 65 e «0,5» 5.
        yield return new object?[] { "6,5", null };
        yield return new object?[] { "0,5", null };
        yield return new object?[] { "1,000", null };
        yield return new object?[] { "-1.5", -1.5m };
        yield return new object?[] { 3301L, 3301m };
        yield return new object?[] { " 6.5 ", 6.5m };
        yield return new object?[] { "0", 0m };
        yield return new object?[] { 4m, 4m };
        yield return new object?[] { 4.25d, 4.25m };
        yield return new object?[] { 7, 7m };
    }

    [Fact]
    public void ParseHours_resta_a_zero_dove_ParseNullableHours_dice_null()
    {
        Assert.Equal(0m, DbOdbcService.ParseHours(""));
        Assert.Equal(0m, DbOdbcService.ParseHours(null));
        Assert.Equal(2.5m, DbOdbcService.ParseHours("2.5"));
        Assert.Equal(0m, DbOdbcService.ParseHours("6,5"));
    }

    [Fact]
    public void Ore_con_la_virgola_in_SAP_non_danno_un_falso_unchanged()
    {
        // In SAP «0,5» (testo all'italiana), il portale chiede 5 h: letto come 5, il servizio avrebbe risposto
        // "già allineata" senza scrivere. Illeggibile invece vale null, che non è uguale a niente.
        var state = DbOdbcService.MapTimesheetHoursState(7128, "7121", "N", "Confermato", null, "0,5", "0,5");

        Assert.Null(state.TotalHours);
        Assert.Null(state.BillableHours);
        Assert.NotEqual(TimesheetHoursOutcome.Unchanged,
            TimesheetHoursRules.Decide(state, new() { Hours = 5m, BillableHours = 5m }).Outcome);
    }

    [Fact]
    public void Riga_con_colonne_testuali_diventa_uno_stato_confrontabile()
    {
        var state = DbOdbcService.MapTimesheetHoursState(7128, "7121   ", "N", "Confermato ", " 0 ", "6.5", "4");

        Assert.Equal(7128, state.DocEntry);
        Assert.Equal("7121", state.Code);
        Assert.Equal("N", state.Canceled);
        Assert.Equal("Confermato", state.Status);
        Assert.Equal("0", state.DestEntry);
        Assert.Equal(6.5m, state.TotalHours);
        Assert.Equal(4m, state.BillableHours);
        Assert.False(TimesheetHoursRules.IsBilled(state.Status, state.DestEntry));
    }

    [Fact]
    public void Riga_con_documento_di_destinazione_numerico_risulta_fatturata()
    {
        var state = DbOdbcService.MapTimesheetHoursState(7128, 7121, "N", "Confermato", 3301, 6.5m, 6.5m);

        Assert.Equal("7121", state.Code);
        Assert.Equal("3301", state.DestEntry);
        Assert.True(TimesheetHoursRules.IsBilled(state.Status, state.DestEntry));
    }

    [Fact]
    public void Riga_con_colonne_nulle_ha_ore_null_e_nessun_documento()
    {
        var state = DbOdbcService.MapTimesheetHoursState(7128, null, null, null, null, null, "");

        Assert.Null(state.Code);
        Assert.Null(state.DestEntry);
        Assert.Null(state.TotalHours);
        Assert.Null(state.BillableHours);
        Assert.False(TimesheetHoursRules.IsCanceled(state.Canceled));
        Assert.False(TimesheetHoursRules.IsBilled(state.Status, state.DestEntry));
    }
}

/// <summary>
/// Il <c>Code</c> con cui il Service Layer indirizza la riga si prende SOLO dalla riga con il DocEntry chiesto: le due
/// colonne divergono (la 7128 porta <c>Code</c> «7121»), e scrivere sulla riga sbagliata cambierebbe le ore di un
/// altro timesheet.
/// </summary>
public class TimesheetServiceLayerCodeTests
{
    [Fact]
    public void Il_Code_e_quello_della_riga_con_il_DocEntry_chiesto()
        => Assert.Equal("7121", SapB1ServiceLayerService.ReadServiceLayerHoursState(
            "{\"value\":[{\"DocEntry\":7128,\"Code\":\"7121\",\"U_TimeNrTot\":6.5}]}", 7128)?.Code);

    [Fact]
    public void Una_riga_con_un_altro_DocEntry_non_viene_usata()
        => Assert.Null(SapB1ServiceLayerService.ReadServiceLayerHoursState(
            "{\"value\":[{\"DocEntry\":7121,\"Code\":\"7114\"}]}", 7128));

    [Theory]
    [InlineData("{\"value\":[]}")]
    [InlineData("{\"value\":[{\"DocEntry\":7128,\"Code\":\"  \"}]}")]
    [InlineData("")]
    public void Nessuna_riga_o_Code_vuoto_danno_null(string body)
        => Assert.Null(SapB1ServiceLayerService.ReadServiceLayerHoursState(body, 7128));
}
