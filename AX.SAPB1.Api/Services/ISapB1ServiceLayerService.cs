using AX.SAPB1.Api.Models;

namespace AX.SAPB1.Api.Services
{
    public interface ISapB1ServiceLayerService : IDisposable
    {
        Task<string> GetSessionIdAsync();
        Task<List<Timesheet>> GetTimesheetsAsync();
        Task<Timesheet?> GetTimesheetAsync(string code);
        Task<Timesheet> CreateTimesheetAsync(TimesheetCreateRequest request);
        Task<Timesheet> CreateTimesheetLiteAsync(
            TimesheetCreateRequestLite request,
            ProjectLookupDetail project,
            ActivitySummary activity);
        TimesheetServiceLayerPayload BuildTimesheetLitePayload(
            TimesheetCreateRequestLite request,
            ProjectLookupDetail project,
            ActivitySummary activity);
        Task<Timesheet> UpdateTimesheetAsync(TimesheetUpdateRequest request);

        /// <summary>
        /// Aggiorna SOLO le ore (<c>U_TimeNrTot</c> = <c>request.Hours</c>, <c>U_TimeNrNet</c> =
        /// <c>request.BillableHours</c>, <c>U_TimeNrNF</c> = la differenza) della riga di timesheet con il
        /// <c>DocEntry</c> indicato. Subito prima del PATCH rilegge la riga dal Service Layer e riesegue
        /// <c>TimesheetHoursRules.Decide</c> con la stessa richiesta: se la riga nel frattempo è stata fatturata,
        /// annullata o modificata in SAP (o ha già i valori chiesti) NON scrive e restituisce quella decisione.
        /// Non scrive nemmeno se <paramref name="cancellationToken"/> è già annullato (chiamante andato via): in quel
        /// caso lancia <see cref="OperationCanceledException"/>. Esito grezzo, non lancia su un rifiuto di SAP.
        /// </summary>
        Task<Timesheets.TimesheetHoursWriteResult> UpdateTimesheetHoursAsync(
            int docEntry, TimesheetHoursUpdateRequest request, TimesheetHoursState? known, CancellationToken cancellationToken = default);

        Task<bool> DeleteTimesheetAsync(string code);

        /// <summary>
        /// Crea una BOZZA di fattura A/R (oggetto SAP B1 Drafts, DocObjectCode = oInvoices) a partire
        /// dal payload del portale AX.360, valorizzando gli UDF di correlazione. La bozza dovrà essere
        /// confermata manualmente in SAP per diventare fattura definitiva.
        /// </summary>
        Task<ErpInvoicePushResult> CreateInvoiceDraftAsync(ErpInvoicePushDto dto);

        /// <summary>
        /// Garantisce l'esistenza dei campi utente AX.360 (InvId/InvNum/DocType) sulle tabelle dei
        /// documenti di marketing (OINV, ORDR e ODRF), così che il valore si propaghi da bozza a definitivo.
        /// Idempotente e best-effort: non lancia se i campi esistono già o se mancano i permessi.
        /// </summary>
        Task EnsureAx360UserFieldsAsync();

        // ── Documenti di vendita e progetti contabili (portale AX.360) ──
        // Esito grezzo, mai eccezioni su un rifiuto di SAP: decide il chiamante.

        /// <summary>POST del documento su <c>Invoices</c>, <c>Orders</c> o <c>Drafts</c>.</summary>
        Task<SalesDocuments.ServiceLayerResponse> CreateSalesDocumentAsync(string entity, IReadOnlyDictionary<string, object?> payload);

        /// <summary>Rilettura di DocEntry/DocNum/DocTotal/VatSum/DocDueDate di un documento.</summary>
        Task<SalesDocuments.ServiceLayerResponse> GetSalesDocumentAsync(string entity, int docEntry);

        /// <summary>Upload multipart in <c>Attachments2</c>; la risposta porta <c>AbsoluteEntry</c>.</summary>
        Task<SalesDocuments.ServiceLayerResponse> UploadAttachmentsAsync(IReadOnlyList<SalesDocuments.ServiceLayerFile> files);

        /// <summary>POST <c>Projects</c> (progetto contabile OPRJ).</summary>
        Task<SalesDocuments.ServiceLayerResponse> CreateFinancialProjectAsync(IReadOnlyDictionary<string, object?> payload);
    }
}
