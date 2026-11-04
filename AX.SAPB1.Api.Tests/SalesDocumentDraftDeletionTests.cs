using AX.SAPB1.Api.Models;
using AX.SAPB1.Api.Services;
using AX.SAPB1.Api.Services.SalesDocuments;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AX.SAPB1.Api.Tests;

/// <summary>
/// Revisione degli ordini in bozza del portale (prima tornata T&amp;M, 2-4/11/2026). Due richieste
/// dell'amministrazione:
/// <list type="bullet">
/// <item>annullare nel portale un documento ancora in bozza deve eliminare la bozza in SAP (prima andava eliminata a
/// mano dal client), ma MAI un definitivo, che si annulla in SAP e non si cancella;</item>
/// <item>l'allegato deve avere «Copia nei documenti di destinazione» acceso, così il riepilogo interventi passa
/// dall'ordine alla fattura (le 23 righe del 2/11 avevano <c>ATC1.CopyToTrgt = N</c>).</item>
/// </list>
/// </summary>
public class SalesDocumentDraftDeletionTests
{
    private static ExistingSalesDocument Doc(string table, string kind, string status, int entry, string canceled = "N", bool converted = false)
        => new() { Table = table, DocumentKind = kind, Status = status, DocEntry = entry, DocNum = entry * 10, Canceled = canceled, ConvertedDraft = converted };

    // ── Regola ──

    [Fact]
    public void Una_bozza_viva_si_elimina()
    {
        var plan = SalesDocumentPayloadBuilder.PlanDraftDeletion(new[] { Doc("ODRF", "order", "draft", 4137) });

        Assert.Equal(SalesDocumentPayloadBuilder.DraftDeletionDecision.Delete, plan.Decision);
        Assert.Equal(new[] { 4137 }, plan.DraftEntries);
    }

    [Fact]
    public void Un_definitivo_valido_non_si_elimina()
    {
        var plan = SalesDocumentPayloadBuilder.PlanDraftDeletion(new[] { Doc("ORDR", "order", "posted", 900) });

        Assert.Equal(SalesDocumentPayloadBuilder.DraftDeletionDecision.Refuse, plan.Decision);
        Assert.Empty(plan.DraftEntries);
        Assert.Contains("9000", plan.Reason);
    }

    [Fact]
    public void Una_bozza_gia_trasformata_nel_definitivo_non_si_elimina()
    {
        // La bozza chiusa resta in ODRF accanto al definitivo: eliminarla non annullerebbe niente, e il portale
        // libererebbe le ore di un ordine vero.
        var plan = SalesDocumentPayloadBuilder.PlanDraftDeletion(new[]
        {
            Doc("ODRF", "order", "draft", 4140, converted: true),
            Doc("ORDR", "order", "posted", 905),
        });

        Assert.Equal(SalesDocumentPayloadBuilder.DraftDeletionDecision.Refuse, plan.Decision);
    }

    [Fact]
    public void Senza_bozze_vive_non_c_e_niente_da_eliminare()
    {
        Assert.Equal(SalesDocumentPayloadBuilder.DraftDeletionDecision.NothingToDelete,
            SalesDocumentPayloadBuilder.PlanDraftDeletion(Array.Empty<ExistingSalesDocument>()).Decision);
        Assert.Equal(SalesDocumentPayloadBuilder.DraftDeletionDecision.NothingToDelete,
            SalesDocumentPayloadBuilder.PlanDraftDeletion(new[] { Doc("ORDR", "order", "posted", 901, canceled: "Y") }).Decision);
    }

    // Risposta reale di GET Attachments2(7476) in produzione, 4/11/2026 (le righe non hanno LineNum).
    private const string AttachmentEntryJson = @"{""AbsoluteEntry"":7476,""Attachments2_Lines"":[
        {""SourcePath"":""/tmp/sap_b1_b1service0/ServiceLayer/Attachments2/"",""FileName"":""Area Edile S.r.l - Attivita ottobre 2026_12f1e47d"",""FileExtension"":""pdf"",""AttachmentDate"":""2026-11-04"",""Override"":""tNO"",""FreeText"":null,""CopyToTargetDoc"":""tNO"",""U_CorCategory"":null},
        {""SourcePath"":""/tmp/sap_b1_b1service0/ServiceLayer/Attachments2/"",""FileName"":""Secondo"",""FileExtension"":""pdf"",""Override"":""tNO"",""FreeText"":""nota"",""CopyToTargetDoc"":""tNO""}]}";

    [Fact]
    public void Il_flag_di_copia_rimanda_ogni_riga_completa_nello_stesso_ordine()
    {
        var patch = SalesDocumentPayloadBuilder.BuildCopyToTargetPatch(JObject.Parse(AttachmentEntryJson));
        var lines = (JArray)JObject.FromObject(patch!)["Attachments2_Lines"]!;

        Assert.Equal(2, lines.Count);
        Assert.Equal("Area Edile S.r.l - Attivita ottobre 2026_12f1e47d", (string?)lines[0]["FileName"]);
        Assert.Equal("/tmp/sap_b1_b1service0/ServiceLayer/Attachments2/", (string?)lines[0]["SourcePath"]);
        Assert.Equal("pdf", (string?)lines[0]["FileExtension"]);
        Assert.Equal("Secondo", (string?)lines[1]["FileName"]);
        Assert.Equal("nota", (string?)lines[1]["FreeText"]);
        Assert.All(lines, l =>
        {
            Assert.Equal("tYES", (string?)l["CopyToTargetDoc"]);
            // Nel sostituire la riga SAP ricopia il file sulla destinazione, che esiste già.
            Assert.Equal("tYES", (string?)l["Override"]);
            // La chiave che il Service Layer rifiuta («Property 'LineNum' of 'Attachments2_Line' is invalid»).
            Assert.Null(l["LineNum"]);
        });
    }

    [Fact]
    public void Niente_patch_se_il_flag_e_gia_acceso_o_non_ci_sono_righe()
    {
        var gia = JObject.Parse(AttachmentEntryJson.Replace(@"""CopyToTargetDoc"":""tNO""", @"""CopyToTargetDoc"":""tYES"""));
        Assert.Null(SalesDocumentPayloadBuilder.BuildCopyToTargetPatch(gia));
        Assert.Null(SalesDocumentPayloadBuilder.BuildCopyToTargetPatch(JObject.Parse(@"{""AbsoluteEntry"":7451}")));
        Assert.Null(SalesDocumentPayloadBuilder.BuildCopyToTargetPatch(null));
    }

    // ── Servizio ──

    private sealed class Sap
    {
        public List<ExistingSalesDocument> Docs { get; } = new();
        public List<int> Deleted { get; } = new();
        public List<(int Entry, IReadOnlyDictionary<string, object?> Payload)> Patches { get; } = new();
        public bool RefuseDelete { get; set; }
        public bool RefusePatch { get; set; }
        public bool RefuseGetAttachments { get; set; }
        public string AttachmentEntry { get; set; } = AttachmentEntryJson;

        private IDbOdbcService Db() => InterfaceFake<IDbOdbcService>.Create(new()
        {
            ["GetUserFieldColumnsAsync"] = _ => Task.FromResult<IReadOnlySet<string>>(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "U_AX360_InvId", "U_AX360_InvNum" }),
            ["GetColumnLengthsAsync"] = _ => Task.FromResult<IReadOnlyDictionary<string, int>>(new Dictionary<string, int>()),
            ["FindSalesDocumentsByCorrelationIdAsync"] = _ => Task.FromResult<IReadOnlyList<ExistingSalesDocument>>(Docs.ToList()),
            ["FindAttachmentsByFileNamesAsync"] = _ => Task.FromResult<IReadOnlyList<AttachmentFileRow>>(Array.Empty<AttachmentFileRow>()),
        });

        private ISapB1ServiceLayerService ServiceLayer() => InterfaceFake<ISapB1ServiceLayerService>.Create(new()
        {
            ["DeleteDraftAsync"] = a =>
            {
                if (RefuseDelete)
                    return Task.FromResult(new ServiceLayerResponse(400, false, @"{""error"":{""code"":-1,""message"":{""value"":""Documento bloccato""}}}"));
                Deleted.Add((int)a[0]!);
                return Task.FromResult(new ServiceLayerResponse(204, true, string.Empty));
            },
            ["UploadAttachmentsAsync"] = _ => Task.FromResult(new ServiceLayerResponse(201, true,
                @"{""AbsoluteEntry"":7500,""Attachments2_Lines"":[{""LineNum"":1}]}")),
            ["GetAttachmentsAsync"] = _ => Task.FromResult(RefuseGetAttachments
                ? new ServiceLayerResponse(404, false, @"{""error"":{""code"":-2028,""message"":{""value"":""No matching records found""}}}")
                : new ServiceLayerResponse(200, true, AttachmentEntry)),
            ["ReplaceAttachmentLinesAsync"] = a =>
            {
                Patches.Add(((int)a[0]!, (IReadOnlyDictionary<string, object?>)a[1]!));
                return Task.FromResult(RefusePatch
                    ? new ServiceLayerResponse(400, false, @"{""error"":{""code"":-1,""message"":{""value"":""Proprietà sconosciuta""}}}")
                    : new ServiceLayerResponse(204, true, string.Empty));
            },
            ["CreateSalesDocumentAsync"] = _ => Task.FromResult(new ServiceLayerResponse(201, true,
                @"{""DocEntry"":4200,""DocNum"":2600117,""DocTotal"":160.13,""VatSum"":28.88}")),
        });

        public SalesDocumentService Service(bool attachments = false)
        {
            var config = TestCompanies.Config(attachments
                ? new Dictionary<string, string?> { ["SapB1:SalesDocuments:Attachments:Enabled"] = "true" }
                : null);
            return new(Db(), ServiceLayer(), config, NullLogger<SalesDocumentService>.Instance,
                TestCompanies.Fixed(new AX.SAPB1.Api.Companies.CompanyRegistry(config).Primary));
        }
    }

    [Fact]
    public async Task Elimina_la_bozza_viva_della_correlazione()
    {
        var sap = new Sap();
        sap.Docs.Add(Doc("ODRF", "order", "draft", 4137));

        var outcome = await sap.Service().DeleteDraftAsync(Guid.NewGuid().ToString());

        Assert.Equal(200, outcome.StatusCode);
        Assert.True(outcome.Result.Deleted);
        Assert.Equal(new[] { 4137 }, outcome.Result.DocEntries);
        Assert.Equal(new[] { 4137 }, sap.Deleted);
    }

    [Fact]
    public async Task Un_definitivo_risponde_409_e_non_tocca_SAP()
    {
        var sap = new Sap();
        sap.Docs.Add(Doc("ORDR", "order", "posted", 905));

        var outcome = await sap.Service().DeleteDraftAsync(Guid.NewGuid().ToString());

        Assert.Equal(409, outcome.StatusCode);
        Assert.False(outcome.Result.Deleted);
        Assert.Empty(sap.Deleted);
    }

    [Fact]
    public async Task Una_bozza_gia_eliminata_risponde_200_senza_eliminare()
    {
        var outcome = await new Sap().Service().DeleteDraftAsync(Guid.NewGuid().ToString());

        Assert.Equal(200, outcome.StatusCode);
        Assert.False(outcome.Result.Found);
        Assert.False(outcome.Result.Deleted);
    }

    [Fact]
    public async Task Se_SAP_rifiuta_l_eliminazione_risponde_502()
    {
        var sap = new Sap { RefuseDelete = true };
        sap.Docs.Add(Doc("ODRF", "order", "draft", 4137));

        var outcome = await sap.Service().DeleteDraftAsync(Guid.NewGuid().ToString());

        Assert.Equal(502, outcome.StatusCode);
        Assert.Contains("Documento bloccato", outcome.Result.ErrorMessage);
    }

    private static SalesDocumentRequest RequestWithAttachment() => new()
    {
        CorrelationId = Guid.NewGuid().ToString(),
        PortalNumber = "FT-2026-0030",
        DocumentKind = "order",
        Posting = "draft",
        CardCode = "IT00230",
        DocDate = new DateTime(2026, 10, 31),
        Lines = new()
        {
            new SalesDocumentLineRequest { ItemCode = "ATT_MTF_CONSULENZA", Description = "Consulenza", Quantity = 1.75m, UnitPrice = 75m },
        },
        Attachments = new()
        {
            new SalesDocumentAttachment { FileName = "FlavourArt - Attivita ottobre 2026.pdf", ContentType = "application/pdf", ContentBase64 = Convert.ToBase64String(new byte[] { 0x25, 0x50, 0x44, 0x46 }) },
        },
    };

    [Fact]
    public async Task L_allegato_nasce_con_la_copia_nei_documenti_di_destinazione()
    {
        var sap = new Sap();

        var outcome = await sap.Service(attachments: true).CreateAsync(RequestWithAttachment());

        Assert.Equal(201, outcome.StatusCode);
        Assert.Equal(7500, outcome.Result.AttachmentEntry);
        var (entry, payload) = Assert.Single(sap.Patches);
        Assert.Equal(7500, entry);
        var lines = (JArray)JObject.FromObject(payload)["Attachments2_Lines"]!;
        Assert.Equal(2, lines.Count);
        Assert.All(lines, l => Assert.Equal("tYES", (string?)l["CopyToTargetDoc"]));
        Assert.Equal("Area Edile S.r.l - Attivita ottobre 2026_12f1e47d", (string?)lines[0]["FileName"]);
        Assert.Empty(outcome.Result.Warnings);
    }

    [Fact]
    public async Task Se_il_flag_non_si_accende_il_documento_nasce_lo_stesso_con_un_avviso()
    {
        var sap = new Sap { RefusePatch = true };

        var outcome = await sap.Service(attachments: true).CreateAsync(RequestWithAttachment());

        Assert.Equal(201, outcome.StatusCode);
        Assert.Equal(7500, outcome.Result.AttachmentEntry);
        Assert.Null(outcome.Result.AttachmentError);
        Assert.Contains(outcome.Result.Warnings, w => w.Contains("Copia nei documenti di destinazione"));
    }

    [Fact]
    public async Task Se_la_voce_allegati_non_si_rilegge_niente_patch_e_un_avviso()
    {
        var sap = new Sap { RefuseGetAttachments = true };

        var outcome = await sap.Service(attachments: true).CreateAsync(RequestWithAttachment());

        Assert.Equal(201, outcome.StatusCode);
        Assert.Equal(7500, outcome.Result.AttachmentEntry);
        Assert.Empty(sap.Patches);
        Assert.Contains(outcome.Result.Warnings, w => w.Contains("Copia nei documenti di destinazione"));
    }

    [Fact]
    public async Task Con_il_flag_gia_acceso_non_si_riscrive_la_voce()
    {
        var sap = new Sap { AttachmentEntry = AttachmentEntryJson.Replace(@"""CopyToTargetDoc"":""tNO""", @"""CopyToTargetDoc"":""tYES""") };

        var outcome = await sap.Service(attachments: true).CreateAsync(RequestWithAttachment());

        Assert.Equal(201, outcome.StatusCode);
        Assert.Empty(sap.Patches);
        Assert.Empty(outcome.Result.Warnings);
    }
}
