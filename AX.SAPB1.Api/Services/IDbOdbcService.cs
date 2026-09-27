using AX.SAPB1.Api.Models;

namespace AX.SAPB1.Api.Services
{
    public interface IDbOdbcService
    {
        Task<IEnumerable<Timesheet>> GetTimesheetsAsync();
        Task<Timesheet?> GetTimesheetByIdAsync(int docEntry);
        Task<IEnumerable<Timesheet>> GetTimesheetsByEmployeeAsync(string employeeId);
        Task<IEnumerable<Timesheet>> GetTimesheetsByProjectAsync(string projectId);
        Task<IEnumerable<Timesheet>> GetTimesheetsByDateRangeAsync(DateTime startDate, DateTime endDate);
        Task<IEnumerable<Timesheet>> GetTimesheetsByEmployeeAndDateRangeAsync(string employeeId, DateTime startDate, DateTime endDate);
        Task<string> GetNextTimesheetCodeAsync();

        /// <summary>
        /// Stato di fatturazione delle righe di timesheet nella finestra [from, to] (per <c>U_Date</c>),
        /// con la fattura che le porta quando esiste. Sola lettura: non scrive mai verso SAP.
        /// </summary>
        Task<IEnumerable<TimesheetBillingState>> GetTimesheetBillingStatesAsync(DateTime from, DateTime to);

        // Lookups
        Task<IEnumerable<CustomerSummary>> GetCustomersAsync();

        /// <summary>Profili anagrafici clienti estesi (ERP-neutri) per il mirror ExternalCustomerProfile del portale.</summary>
        Task<IEnumerable<CustomerProfile>> GetCustomerProfilesAsync();
        Task<CustomerProfile?> GetCustomerProfileAsync(string cardCode);
        Task<IEnumerable<ContactSummary>> GetContactsByCustomerAsync(string cardCode);
        Task<IEnumerable<ProjectSummary>> GetProjectsAsync();
        Task<ProjectLookupDetail?> GetProjectLookupDetailByCodeAsync(string projectCode);
        Task<IEnumerable<ActivitySummary>> GetActivitiesByProjectAsync(string projectCode);
        Task<IEnumerable<ProjectSummary>> GetProjectsByCustomerAsync(string cardCode);
        Task<IEnumerable<ResourceSummary>> GetResourcesAsync();

        // Aggregations
        Task<ActivityTimeTotal?> GetActivityTimeTotAsync(string projectId, string activityId);

        // ERP financial mirror (read): fatture A/R definitive (OINV) e partitario clienti (JDT1).
        Task<IEnumerable<ErpInvoiceDto>> GetInvoicesAsync(DateTime? since);
        Task<IEnumerable<ErpLedgerEntryDto>> GetLedgerAsync(string? customerCode, DateTime? since);

        /// <summary>
        /// Cerca una bozza (ODRF) o una fattura definitiva (OINV) già marcata con il codice interno
        /// AX.360 indicato nell'UDF di correlazione. Usato per evitare doppioni in fase di push.
        /// </summary>
        Task<ExistingErpDocument?> FindDocumentByCorrelationIdAsync(string ax360InvoiceId);

        // ── Documenti di vendita dal portale (fattura/ordine, bozza/definitivo) ──

        /// <summary>
        /// Documenti di vendita (OINV, ORDR o bozze ODRF di tipo 13/17) già marcati con il correlationId in
        /// <c>U_AX360_InvId</c>, cercati solo nelle <paramref name="tables"/> che hanno il campo, in ordine
        /// deterministico. La scelta fra più candidati è di SalesDocumentPayloadBuilder.SelectExisting.
        /// </summary>
        Task<IReadOnlyList<ExistingSalesDocument>> FindSalesDocumentsByCorrelationIdAsync(string correlationId, IReadOnlyCollection<string> tables);

        /// <summary>Colonne della tabella con la lunghezza massima (SYS.TABLE_COLUMNS). Vuoto se non leggibile.</summary>
        Task<IReadOnlyDictionary<string, int>> GetColumnLengthsAsync(string table, bool forceRefresh = false);

        /// <summary>Campi utente (nome fisico U_…) presenti sulla tabella: SYS.TABLE_COLUMNS ∪ CUFD. Lancia se nessuna fonte è leggibile.</summary>
        Task<IReadOnlySet<string>> GetUserFieldColumnsAsync(string table, bool forceRefresh = false);

        /// <summary>Unità di misura (OUOM) e gruppi UdM degli articoli (OITM/UGP1) per le righe di un documento.</summary>
        Task<SalesDocuments.UomResolution> ResolveUnitsOfMeasureAsync(IReadOnlyCollection<string> itemCodes, IReadOnlyCollection<string> uomCodes);

        /// <summary>Ordini cliente correlati al portale (U_AX360_InvId valorizzato), con stato, residuo e fatture tratte.</summary>
        Task<IEnumerable<ErpSalesOrderDto>> GetSalesOrdersAsync(DateTime? since);

        /// <summary>Articoli (OITM); con <paramref name="sellableOnly"/> solo quelli di vendita attivi.</summary>
        Task<IEnumerable<ErpItemDto>> GetSellableItemsAsync(bool sellableOnly);

        /// <summary>Codici dei progetti contabili (OPRJ) che rispondono al pattern LIKE (ESCAPE '\').</summary>
        Task<IReadOnlyList<string>> GetFiscalProjectCodesLikeAsync(string likePattern);

        Task<bool> FiscalProjectExistsAsync(string code);

        /// <summary>
        /// Righe di ATC1 (allegati) con uno dei nomi di file indicati (senza estensione): servono a riusare la voce
        /// di Attachments2 caricata da un tentativo precedente dello stesso documento.
        /// </summary>
        Task<IReadOnlyList<SalesDocuments.AttachmentFileRow>> FindAttachmentsByFileNamesAsync(IReadOnlyCollection<string> fileNames);

        // ── Contabilità generale (lettura) ────────────────────────────────────
        // Nota: distinta da GetLedgerAsync, che è il PARTITARIO CLIENTI (scadenzario/esposizione).
        // Questi metodi leggono il conto economico riga per riga: sono cose diverse, il nome inganna.

        Task<IEnumerable<GlAccountDto>> GetGlAccountsAsync();
        Task<IEnumerable<GlFiscalPeriodDto>> GetGlFiscalPeriodsAsync();
        Task<IEnumerable<GlFiscalProjectDto>> GetGlFiscalProjectsAsync();
        Task<IEnumerable<GlDimensionDto>> GetGlDimensionsAsync();
        Task<IEnumerable<GlDistributionRuleDto>> GetGlDistributionRulesAsync();
        Task<IEnumerable<GlLineDto>> GetGlLinesAsync(DateTime from, DateTime to, int skip = 0, int take = 0);
        Task<IEnumerable<GlLineDto>> GetGlLinesByEntryIdsAsync(IReadOnlyCollection<int> entryIds);

        /// <summary>Fattura/NC di origine (testata + righe prodotti) di una registrazione JDT1, per l'anteprima. Null se il tipo non ha un documento con righe.</summary>
        Task<ErpInvoiceDto?> GetSourceDocumentAsync(int transId, string? docType);

        // ── Contabilità generale (scrittura) ──────────────────────────────────

        /// <summary>
        /// Aggiorna progetto e dimensioni analitiche sulle righe contabili indicate, in una sola
        /// transazione. È l'UNICO write path SQL del servizio: tutto il resto passa dal Service Layer.
        /// </summary>
        Task<GlAttributionResult> UpdateGlAttributionAsync(GlAttributionRequest request);
    }
}
