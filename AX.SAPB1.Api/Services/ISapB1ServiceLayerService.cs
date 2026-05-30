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
        Task<bool> DeleteTimesheetAsync(string code);

        /// <summary>
        /// Crea una BOZZA di fattura A/R (oggetto SAP B1 Drafts, DocObjectCode = oInvoices) a partire
        /// dal payload del portale AX.360, valorizzando gli UDF di correlazione. La bozza dovrà essere
        /// confermata manualmente in SAP per diventare fattura definitiva.
        /// </summary>
        Task<ErpInvoicePushResult> CreateInvoiceDraftAsync(ErpInvoicePushDto dto);

        /// <summary>
        /// Garantisce l'esistenza dei campi utente AX.360 (InvId/InvNum/DocType) sulle tabelle dei
        /// documenti di marketing (OINV e ODRF), così che il valore si propaghi da bozza a definitivo.
        /// Idempotente e best-effort: non lancia se i campi esistono già o se mancano i permessi.
        /// </summary>
        Task EnsureAx360UserFieldsAsync();
    }
}
