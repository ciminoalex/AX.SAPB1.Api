using System.Net;
using AX.SAPB1.Api.Models;
using AX.SAPB1.Api.Services;
using AX.SAPB1.Api.Services.Timesheets;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AX.SAPB1.Api.Tests;

/// <summary>
/// La scrittura delle ore lato Service Layer, con un Service Layer finto a livello HTTP. Il punto: la decisione presa
/// sul dato ODBC può essere vecchia (sessione da validare, login appeso), quindi la riga restituita dalla ricerca del
/// <c>Code</c> si ricontrolla subito prima del PATCH. Una riga fatturata, annullata o modificata in SAP nel frattempo
/// NON riceve il PATCH; e nemmeno una richiesta che il portale ha già abbandonato.
/// </summary>
public class TimesheetHoursServiceLayerTests
{
    private const string Lookup = "SGS_PRJ_OTMS?$filter=DocEntry eq 7128";

    /// <summary>Service Layer finto: sessione valida, ricerca configurabile, PATCH registrati.</summary>
    private sealed class FakeServiceLayer : HttpMessageHandler
    {
        public string LookupBody { get; set; } = Row();
        public HttpStatusCode LookupStatus { get; set; } = HttpStatusCode.OK;
        public HttpStatusCode PatchStatus { get; set; } = HttpStatusCode.NoContent;
        public Action? OnLookup { get; set; }
        public List<(string Method, string Path, string? Body)> Calls { get; } = new();

        public IEnumerable<(string Method, string Path, string? Body)> Patches => Calls.Where(c => c.Method == "PATCH");

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var path = Uri.UnescapeDataString(uri.AbsolutePath + uri.Query).Replace("/b1s/v1/", string.Empty);
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(CancellationToken.None);
            Calls.Add((request.Method.Method, path, body));

            if (request.Method == HttpMethod.Get && path == "UserFields")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
            if (request.Method == HttpMethod.Get && path == Lookup)
            {
                OnLookup?.Invoke();
                return new HttpResponseMessage(LookupStatus) { Content = new StringContent(LookupBody) };
            }
            if (request.Method == HttpMethod.Patch)
                return new HttpResponseMessage(PatchStatus) { Content = new StringContent(string.Empty) };
            throw new InvalidOperationException($"Chiamata al Service Layer non prevista dal test: {request.Method} {path}");
        }
    }

    private static string Row(
        string status = "Confermato", object? destEntry = null, string canceled = "N", object? total = null, object? net = null)
        => new JObject
        {
            ["value"] = new JArray(new JObject
            {
                ["DocEntry"] = 7128,
                ["Code"] = "7121",
                ["Canceled"] = canceled,
                ["U_Status"] = status,
                ["U_DestEntry"] = destEntry is null ? JValue.CreateNull() : JToken.FromObject(destEntry),
                ["U_TimeNrTot"] = JToken.FromObject(total ?? 6.5m),
                ["U_TimeNrNet"] = JToken.FromObject(net ?? 6.5m),
            }),
        }.ToString();

    private static SapB1ServiceLayerService Service(FakeServiceLayer handler)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["SapB1:ServiceLayerUrl"] = "https://sl.test/b1s/v1/" })
            .Build();
        // Sessione già in cache per l'account di servizio: il login non fa parte di questi test.
        var cache = new MemoryCache(new MemoryCacheOptions());
        cache.Set("SapB1:SessionCookie:service-account", "B1SESSION=test");
        cache.Set("SapB1:SessionId:service-account", "test");
        return new SapB1ServiceLayerService(
            new HttpClient(handler), configuration, NullLogger<SapB1ServiceLayerService>.Instance,
            InterfaceFake<IDbOdbcService>.Create(new()), cache, new HttpContextAccessor(),
            InterfaceFake<ICredentialStore>.Create(new()));
    }

    private static TimesheetHoursUpdateRequest Request(decimal? expHours = 6.5m, decimal? expBillable = 6.5m)
        => new() { Hours = 6.5m, BillableHours = 4m, ExpectedHours = expHours, ExpectedBillableHours = expBillable };

    // Quello che il controller ha letto via ODBC: riga confermata, 6,5/6,5.
    private static readonly TimesheetHoursState Known = new()
    {
        DocEntry = 7128, Code = "7121", Canceled = "N", Status = "Confermato", DestEntry = null, TotalHours = 6.5m, BillableHours = 6.5m,
    };

    [Theory]
    [InlineData("Fatturato", null, "N", "billed")]
    [InlineData("Confermato", 3301, "N", "billed")]
    [InlineData("Confermato", "3301", "N", "billed")]
    [InlineData("Confermato", null, "Y", "canceled")]
    [InlineData("Confermato", null, "tYES", "canceled")]
    public async Task Riga_fatturata_o_annullata_alla_rilettura_non_riceve_il_PATCH(string status, object? destEntry, string canceled, string atteso)
    {
        var sl = new FakeServiceLayer { LookupBody = Row(status, destEntry, canceled) };
        using var service = Service(sl);

        var result = await service.UpdateTimesheetHoursAsync(7128, Request(), Known);

        Assert.NotNull(result.Superseded);
        Assert.Equal(atteso, result.Superseded!.Outcome);
        Assert.Equal(409, result.Superseded.HttpStatus);
        Assert.Null(result.Response);
        Assert.Empty(sl.Patches);
    }

    [Fact]
    public async Task Ore_modificate_in_SAP_dopo_la_lettura_danno_changed_in_erp_senza_PATCH()
    {
        // Testo, come arriva su questo impianto: 5 h fatturabili messe a mano dopo la lettura ODBC.
        var sl = new FakeServiceLayer { LookupBody = Row(total: "6.5", net: "5") };
        using var service = Service(sl);

        var result = await service.UpdateTimesheetHoursAsync(7128, Request(), Known);

        Assert.Equal(TimesheetHoursOutcome.ChangedInErp, result.Superseded?.Outcome);
        Assert.Equal(5m, result.Superseded!.CurrentBillableHours);
        Assert.Empty(sl.Patches);
    }

    [Fact]
    public async Task Valori_gia_presenti_alla_rilettura_danno_unchanged_senza_PATCH()
    {
        var sl = new FakeServiceLayer { LookupBody = Row(total: 6.5m, net: 4m) };
        using var service = Service(sl);

        var result = await service.UpdateTimesheetHoursAsync(7128, Request(), Known);

        Assert.Equal(TimesheetHoursOutcome.Unchanged, result.Superseded?.Outcome);
        Assert.Equal(200, result.Superseded!.HttpStatus);
        Assert.Empty(sl.Patches);
    }

    [Fact]
    public async Task Riga_ancora_scrivibile_riceve_solo_i_tre_campi_ore_sul_suo_Code()
    {
        var sl = new FakeServiceLayer();
        using var service = Service(sl);

        var result = await service.UpdateTimesheetHoursAsync(7128, Request(), Known);

        Assert.Null(result.Superseded);
        Assert.True(result.Response!.IsSuccess);
        Assert.Equal(6.5m, result.Fresh!.TotalHours);
        var patch = Assert.Single(sl.Patches);
        Assert.Equal("SGS_PRJ_OTMS('7121')", patch.Path);
        var body = JObject.Parse(patch.Body!);
        Assert.Equal(new[] { "U_TimeNrTot", "U_TimeNrNet", "U_TimeNrNF" }, body.Properties().Select(p => p.Name));
        Assert.Equal(6.5m, body.Value<decimal>("U_TimeNrTot"));
        Assert.Equal(4m, body.Value<decimal>("U_TimeNrNet"));
        Assert.Equal(2.5m, body.Value<decimal>("U_TimeNrNF"));
    }

    [Fact]
    public async Task Colonna_non_esposta_dal_Service_Layer_prende_il_valore_letto_via_ODBC()
    {
        // La risposta non porta U_Status né U_DestEntry: vale quello che ha visto ODBC («Fatturato»), non "assente".
        var sl = new FakeServiceLayer
        {
            LookupBody = "{\"value\":[{\"DocEntry\":7128,\"Code\":\"7121\",\"U_TimeNrTot\":6.5,\"U_TimeNrNet\":6.5}]}",
        };
        using var service = Service(sl);
        var known = new TimesheetHoursState { DocEntry = 7128, Code = "7121", Canceled = "N", Status = "Fatturato", TotalHours = 6.5m, BillableHours = 6.5m };

        var result = await service.UpdateTimesheetHoursAsync(7128, Request(), known);

        Assert.Equal(TimesheetHoursOutcome.Billed, result.Superseded?.Outcome);
        Assert.Empty(sl.Patches);
    }

    [Fact]
    public async Task Chiamante_andato_via_durante_la_ricerca_nessun_PATCH()
    {
        using var abbandono = new CancellationTokenSource();
        var sl = new FakeServiceLayer { OnLookup = abbandono.Cancel };
        using var service = Service(sl);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.UpdateTimesheetHoursAsync(7128, Request(), Known, abbandono.Token));

        Assert.Empty(sl.Patches);
    }

    [Fact]
    public async Task Chiamante_gia_andato_via_nessuna_ricerca_e_nessun_PATCH()
    {
        using var abbandono = new CancellationTokenSource();
        abbandono.Cancel();
        var sl = new FakeServiceLayer();
        using var service = Service(sl);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.UpdateTimesheetHoursAsync(7128, Request(), Known, abbandono.Token));

        Assert.DoesNotContain(sl.Calls, c => c.Path.StartsWith("SGS_PRJ_OTMS", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task PATCH_rifiutato_torna_come_esito_grezzo_senza_secondo_invio(HttpStatusCode status)
    {
        // Anche un 401 non porta a login + secondo PATCH: la decisione presa sulla riga riletta invecchierebbe.
        var sl = new FakeServiceLayer { PatchStatus = status };
        using var service = Service(sl);

        var result = await service.UpdateTimesheetHoursAsync(7128, Request(), Known);

        Assert.False(result.Response!.IsSuccess);
        Assert.Equal((int)status, result.Response.StatusCode);
        Assert.Single(sl.Patches);
    }

    [Fact]
    public async Task Ricerca_fallita_non_diventa_riga_non_trovata_e_non_scrive()
    {
        var sl = new FakeServiceLayer { LookupStatus = HttpStatusCode.InternalServerError, LookupBody = "{\"error\":{}}" };
        using var service = Service(sl);

        var result = await service.UpdateTimesheetHoursAsync(7128, Request(), Known);

        Assert.Equal(500, result.Response!.StatusCode);
        Assert.Null(result.Superseded);
        Assert.Empty(sl.Patches);
    }

    [Fact]
    public void La_riga_del_Service_Layer_si_legge_con_i_parser_tolleranti()
    {
        var state = SapB1ServiceLayerService.ReadServiceLayerHoursState(
            "{\"value\":[{\"DocEntry\":7128,\"Code\":\"7121 \",\"Canceled\":\"tNO\",\"U_Status\":\" Confermato\"," +
            "\"U_DestEntry\":\"0\",\"U_TimeNrTot\":\"6.5\",\"U_TimeNrNet\":0.1,\"U_TimeNrNF\":\"6,4\"}]}", 7128);

        Assert.NotNull(state);
        Assert.Equal("7121", state!.Code);
        Assert.False(TimesheetHoursRules.IsCanceled(state.Canceled));
        Assert.False(TimesheetHoursRules.IsBilled(state.Status, state.DestEntry));
        Assert.Equal(6.5m, state.TotalHours);
        // Letto come decimal, non come double: 0,1 resta esattamente 0,1.
        Assert.Equal(0.1m, state.BillableHours);
    }

    [Fact]
    public void Ore_nulle_o_illeggibili_nel_Service_Layer_restano_null_e_non_prendono_il_valore_ODBC()
    {
        // La colonna c'è ma è vuota: il dato fresco vince, anche se vale "non lo so".
        var state = SapB1ServiceLayerService.ReadServiceLayerHoursState(
            "{\"value\":[{\"DocEntry\":7128,\"Code\":\"7121\",\"U_TimeNrTot\":null,\"U_TimeNrNet\":\"6,5\"}]}", 7128, Known);

        Assert.Null(state!.TotalHours);
        Assert.Null(state.BillableHours);
        Assert.Equal("Confermato", state.Status);
    }
}
