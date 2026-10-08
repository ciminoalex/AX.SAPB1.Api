using AX.SAPB1.Api.Models;
using AX.SAPB1.Api.Services;
using AX.SAPB1.Api.Services.SalesDocuments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AX.SAPB1.Api.Tests;

/// <summary>
/// Stato del documento per correlazione (<c>GET /api/sales-documents/by-correlation</c>), quello che il portale
/// chiede prima di annullare un suo documento: finché qui risulta valido, il portale non libera ore, SAL e canoni.
/// Due modi in cui la risposta mentiva:
/// <list type="bullet">
/// <item>una bozza già trasformata nel definitivo resta in ODRF con <c>CANCELED = 'N'</c> (misurato su SBO_MTF:
/// 164 bozze di fattura chiuse così) e vinceva su un definitivo annullato in SAP: «bozza valida», annullo negato
/// per sempre;</item>
/// <item>la lettura non aspettava una creazione in volo (il portale rinuncia a 60 secondi, il Service Layer può
/// metterne 100): «non trovato», annullo ammesso, e pochi secondi dopo il documento compariva in SAP.</item>
/// </list>
/// </summary>
public class SalesDocumentStateTests
{
    private static ExistingSalesDocument Doc(string table, string kind, string status, int entry, string canceled = "N", bool converted = false)
        => new() { Table = table, DocumentKind = kind, Status = status, DocEntry = entry, DocNum = entry * 10, Canceled = canceled, ConvertedDraft = converted };

    // ── Bozza già trasformata ──

    [Fact]
    public void Bozza_trasformata_e_fattura_annullata_in_SAP_danno_documento_annullato()
    {
        var scelta = SalesDocumentPayloadBuilder.SelectExisting(new[]
        {
            Doc("ODRF", "invoice", "draft", 1, converted: true),
            Doc("OINV", "invoice", "posted", 50, canceled: "Y"),
            Doc("OINV", "invoice", "posted", 51, canceled: "C"),
        });

        Assert.Equal(50, scelta!.DocEntry);
        var state = SalesDocumentPayloadBuilder.ToState(scelta);
        Assert.True(state.Found);
        Assert.True(state.Cancelled);
        Assert.Equal("posted", state.Status);
    }

    [Fact]
    public void Bozza_trasformata_e_ordine_annullato_in_SAP_danno_documento_annullato()
    {
        var state = SalesDocumentPayloadBuilder.ToState(SalesDocumentPayloadBuilder.SelectExisting(new[]
        {
            Doc("ODRF", "order", "draft", 2, converted: true),
            Doc("ORDR", "order", "posted", 70, canceled: "Y"),
        }, "order"));

        Assert.True(state.Cancelled);
        Assert.Equal("order", state.DocumentKind);
    }

    [Fact]
    public void Bozza_trasformata_e_solo_documento_di_annullamento_danno_documento_annullato()
        => Assert.True(SalesDocumentPayloadBuilder.ToState(SalesDocumentPayloadBuilder.SelectExisting(new[]
        {
            Doc("ODRF", "invoice", "draft", 1, converted: true),
            Doc("OINV", "invoice", "posted", 51, canceled: "C"),
        })).Cancelled);

    [Fact]
    public void Col_definitivo_valido_vince_il_definitivo_anche_sulla_bozza_trasformata()
    {
        var scelta = SalesDocumentPayloadBuilder.SelectExisting(new[]
        {
            Doc("ODRF", "invoice", "draft", 1, converted: true),
            Doc("OINV", "invoice", "posted", 50),
        });
        Assert.Equal(50, scelta!.DocEntry);
        Assert.False(SalesDocumentPayloadBuilder.ToState(scelta).Cancelled);
    }

    [Fact]
    public void Sola_bozza_trasformata_vale_definitivo_non_annullato_senza_i_numeri_della_bozza()
    {
        // Il definitivo nato dalla bozza non porta la correlazione: esiste, ma non se ne sa lo stato. Il portale
        // non deve liberare le ore («annullalo nell'ERP»), e il numero della bozza non è quello del documento.
        var state = SalesDocumentPayloadBuilder.ToState(SalesDocumentPayloadBuilder.SelectExisting(new[]
        {
            Doc("ODRF", "invoice", "draft", 1, converted: true),
        }));

        Assert.True(state.Found);
        Assert.False(state.Cancelled);
        Assert.Equal("posted", state.Status);
        Assert.Null(state.DocEntry);
        Assert.Null(state.DocNum);
    }

    [Fact]
    public void Bozza_aperta_resta_una_bozza_valida()
    {
        var state = SalesDocumentPayloadBuilder.ToState(SalesDocumentPayloadBuilder.SelectExisting(new[] { Doc("ODRF", "invoice", "draft", 1) }));
        Assert.Equal(("draft", false, 1), (state.Status, state.Cancelled, state.DocEntry!.Value));
    }

    // ── Esito incerto ──

    [Fact]
    public void Esito_incerto_vale_per_la_finestra_poi_scade()
    {
        var memory = new UncertainCreations();
        var now = new DateTime(2026, 9, 27, 10, 0, 0, DateTimeKind.Utc);

        Assert.False(memory.IsUncertain("doc-1", now, out _));
        memory.Mark("doc-1", now);

        Assert.True(memory.IsUncertain(" DOC-1 ", now.AddMinutes(5), out var since));
        Assert.Equal(now, since);
        Assert.False(memory.IsUncertain("doc-2", now, out _));
        Assert.False(memory.IsUncertain("doc-1", now + UncertainCreations.Window + TimeSpan.FromSeconds(1), out _));
    }

    // ── Servizio: lock e esito incerto ──

    private sealed class Sap
    {
        public ExistingSalesDocument? Created;
        public readonly TaskCompletionSource Reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Exception? Failure;

        public IDbOdbcService Db() => InterfaceFake<IDbOdbcService>.Create(new()
        {
            ["GetUserFieldColumnsAsync"] = _ => Task.FromResult<IReadOnlySet<string>>(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "U_AX360_InvId", "U_AX360_InvNum" }),
            ["GetColumnLengthsAsync"] = _ => Task.FromResult<IReadOnlyDictionary<string, int>>(new Dictionary<string, int>()),
            ["FindSalesDocumentsByCorrelationIdAsync"] = _ => Task.FromResult<IReadOnlyList<ExistingSalesDocument>>(
                Created == null ? Array.Empty<ExistingSalesDocument>() : new[] { Created }),
        });

        public ISapB1ServiceLayerService ServiceLayer() => InterfaceFake<ISapB1ServiceLayerService>.Create(new()
        {
            ["CreateSalesDocumentAsync"] = _ => CreateAsync(),
        });

        private async Task<ServiceLayerResponse> CreateAsync()
        {
            Reached.TrySetResult();
            await Gate.Task;
            if (Failure != null) throw Failure;
            Created = Doc("ODRF", "invoice", "draft", 5);
            return new ServiceLayerResponse(201, true, @"{""DocEntry"":5,""DocNum"":55,""DocTotal"":388.88,""VatSum"":70.13}");
        }

        public SalesDocumentService Service()
            => new(Db(), ServiceLayer(), new ConfigurationBuilder().Build(), NullLogger<SalesDocumentService>.Instance,
                TestCompanies.Fixed(TestCompanies.Registry().Primary));
    }

    private static SalesDocumentRequest Request(string correlationId) => new()
    {
        CorrelationId = correlationId,
        PortalNumber = "FT-2026-0001",
        DocumentKind = "invoice",
        Posting = "draft",
        CardCode = "IT00387",
        DocDate = new DateTime(2026, 9, 30),
        Lines = new()
        {
            new SalesDocumentLineRequest { ItemCode = "ATT_MTF_CONSULENZA", Description = "Consulenza", Quantity = 4.25m, UnitPrice = 75m },
        },
    };

    [Fact]
    public async Task La_lettura_dello_stato_attende_la_creazione_in_corso()
    {
        var correlation = Guid.NewGuid().ToString();
        var sap = new Sap();
        var create = sap.Service().CreateAsync(Request(correlation));
        await sap.Reached.Task;

        // Un'altra richiesta (il servizio è Scoped: istanza diversa, stesso lock di processo).
        var state = sap.Service().GetStateAsync(correlation);
        await Task.Delay(150);
        Assert.False(state.IsCompleted);

        sap.Gate.SetResult();
        Assert.Equal(201, (await create).StatusCode);
        var result = await state;
        Assert.True(result.Found);
        Assert.Equal(5, result.DocEntry);
    }

    [Fact]
    public async Task Dopo_una_creazione_senza_risposta_non_trovato_non_vale()
    {
        var correlation = Guid.NewGuid().ToString();
        var sap = new Sap { Failure = new TaskCanceledException("timeout verso il Service Layer") };
        sap.Gate.SetResult();

        await Assert.ThrowsAsync<TaskCanceledException>(() => sap.Service().CreateAsync(Request(correlation)));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sap.Service().GetStateAsync(correlation));
        Assert.Contains("non verificabile", ex.Message);

        // Se nel frattempo SAP l'ha registrato, vale il documento.
        sap.Created = Doc("ODRF", "invoice", "draft", 9);
        Assert.True((await sap.Service().GetStateAsync(correlation)).Found);
    }

    [Fact]
    public async Task Senza_creazioni_incerte_non_trovato_resta_non_trovato()
    {
        var state = await new Sap().Service().GetStateAsync(Guid.NewGuid().ToString());
        Assert.False(state.Found);
    }
}
