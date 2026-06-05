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
    }
}
