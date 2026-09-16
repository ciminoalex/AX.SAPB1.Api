using System.Data.Odbc;
using System.Globalization;
using AX.SAPB1.Api.Models;
using Microsoft.AspNetCore.Http;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace AX.SAPB1.Api.Services
{
    /// <summary>
    /// Accesso ODBC diretto a SAP B1 su HANA. La contabilità generale — lettura del conto economico e
    /// scrittura dell'attribuzione analitica — vive nella partial <c>DbOdbcService.Gl.cs</c>.
    /// </summary>
    public partial class DbOdbcService : IDbOdbcService
    {
        // Ponte progetto→contabilità: il campo standard SAP è FIPROJECT; su questo impianto convive con
        // l'UDF U_SGS_PRJ_PrjCode, che su 43 progetti è l'unico valorizzato — dove ci sono entrambi
        // coincidono sempre (verificato su 77 casi, orfani zero). COALESCE + NULLIF perché SAP scrive
        // stringa vuota, non NULL. Presuppone l'alias T. sulla tabella OPMG (verificarlo prima di
        // riusarla altrove). Usata in GetProjectsAsync e GetProjectsByCustomerAsync: un solo punto da
        // cambiare se la derivazione cambia.
        private const string FiscalProjectCodeExpression = @"COALESCE(NULLIF(T.""FIPROJECT"", ''), T.""U_SGS_PRJ_PrjCode"")";

        private readonly string _connectionString;
        private readonly string _schema;
        private readonly IConfiguration _configuration;
        private readonly ILogger<DbOdbcService> _logger;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public DbOdbcService(IConfiguration configuration, ILogger<DbOdbcService> logger, IHttpContextAccessor httpContextAccessor)
        {
            _connectionString = configuration.GetConnectionString("DefaultDatabase")
                ?? throw new ArgumentNullException(nameof(configuration), "DefaultDatabase connection string not found");

            _schema = configuration["SapB1:CompanyDB"]
                ?? throw new ArgumentNullException(nameof(configuration), "Schema not defined");

            _configuration = configuration;
            _logger = logger;
            _httpContextAccessor = httpContextAccessor;
        }

        private async Task<OdbcConnection> CreateOpenConnectionAsync()
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var connection = new OdbcConnection(_connectionString);
            try
            {
                _logger.LogDebug("Opening ODBC connection to schema {Schema}", _schema);
                await connection.OpenAsync();
                stopwatch.Stop();
                _logger.LogInformation("ODBC connection opened in {ElapsedMs} ms", stopwatch.ElapsedMilliseconds);
                return connection;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Failed to open ODBC connection after {ElapsedMs} ms", stopwatch.ElapsedMilliseconds);
                connection.Dispose();
                throw;
            }
        }

        public async Task<IEnumerable<Timesheet>> GetTimesheetsAsync()
        {
            var timesheets = new List<Timesheet>();
            
            try
            {
                using var connection = await CreateOpenConnectionAsync();
                
                var query = $@"
                    SELECT 
                        ""DocEntry"",
                        ""Code"",
                        ""U_ResId"" AS ""ResId"",
                        ""U_CardCode"" AS ""CardCode"",
                        ""U_CardName"" AS ""CardName"",
                        ""U_RefId"" AS ""RefId"",
                        ""U_RefData"" AS ""RefData"",
                        ""U_Project"" AS ""Project"",
                        ""U_ProjectName"" AS ""ProjectName"",
                        ""U_SubProject"" AS ""SubProject"",
                        ""U_Activity"" AS ""Activity"",
                        ""U_ActivityId"" AS ""ActivityId"",
                        ""U_SubActivity"" AS ""SubActivity"",
                        ""U_ActivityName"" AS ""ActivityName"",
                        ""U_Date"" AS ""Date"",
                        ""U_TimeStart"" AS ""TimeStart"",
                        ""U_TimeEnd"" AS ""TimeEnd"",
                        ""U_TimePa"" AS ""TimePa"",
                        ""U_TimeNF"" AS ""TimeNF"",
                        ""U_TimeNrPa"" AS ""TimeNrPa"",
                        ""U_TimeNrNF"" AS ""TimeNrNF"",
                        ""U_TimeNrTot"" AS ""TimeNrTot"",
                        ""U_TimeNrNet"" AS ""TimeNrNet"",
                        ""U_DescExt"" AS ""DescExt"",
                        ""U_DescInt"" AS ""DescInt"",
                        ""U_Status"" AS ""Status""
                    FROM ""{_schema}"".""@SGS_PRJ_OTMS""
                    ORDER BY ""U_Date"" DESC";
                
                using var command = new OdbcCommand(query, connection);
                using var reader = await command.ExecuteReaderAsync();
                
                while (await reader.ReadAsync())
                {
                    timesheets.Add(new Timesheet
                    {
                        DocEntry = reader.IsDBNull(0) ? null : (int)reader.GetInt32(0),
                        Code = reader.IsDBNull(1) ? null : reader.GetString(1),
                        ResId = reader.IsDBNull(2) ? null : reader.GetString(2),
                        CardCode = reader.IsDBNull(3) ? null : reader.GetString(3),
                        CardName = reader.IsDBNull(4) ? null : reader.GetString(4),
                        RefId = reader.IsDBNull(5) ? null : reader.GetString(5),
                        RefData = reader.IsDBNull(6) ? null : reader.GetString(6),
                        Project = reader.IsDBNull(7) ? null : reader.GetString(7),
                        ProjectName = reader.IsDBNull(8) ? null : reader.GetString(8),
                        SubProject = reader.IsDBNull(9) ? null : reader.GetString(9),
                        Activity = reader.IsDBNull(10) ? null : reader.GetString(10),
                        ActivityId = reader.IsDBNull(11) ? null : reader.GetString(11),
                        SubActivity = reader.IsDBNull(12) ? null : reader.GetString(12),
                        ActivityName = reader.IsDBNull(13) ? null : reader.GetString(13),
                        Date = reader.GetDateTime(14),
                        TimeStart = reader.IsDBNull(15) ? null : (int)reader.GetInt16(15),
                        TimeEnd = reader.IsDBNull(16) ? null : (int)reader.GetInt16(16),
                        TimePa = reader.IsDBNull(17) ? null : (int)reader.GetInt16(17),
                        TimeNF = reader.IsDBNull(18) ? null : (int)reader.GetInt16(18),
                        TimeNrPa = reader.IsDBNull(19) ? null : reader.GetDecimal(19),
                        TimeNrNF = reader.IsDBNull(20) ? null : reader.GetDecimal(20),
                        TimeNrTot = reader.IsDBNull(21) ? null : reader.GetDecimal(21),
                        TimeNrNet = reader.IsDBNull(22) ? null : reader.GetDecimal(22),
                        DescExt = reader.IsDBNull(23) ? null : reader.GetString(23),
                        DescInt = reader.IsDBNull(24) ? null : reader.GetString(24),
                        Status = reader.IsDBNull(25) ? null : reader.GetString(25),
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving timesheets from database");
                throw;
            }
            
            return timesheets;
        }

        public async Task<Timesheet?> GetTimesheetByIdAsync(int docEntry)
        {
            try
            {
                using var connection = new OdbcConnection(_connectionString);
                await connection.OpenAsync();
                
                var query = $@"
                    SELECT 
                        ""DocEntry"",
                        ""Code"",
                        ""U_ResId"" AS ""ResId"",
                        ""U_CardCode"" AS ""CardCode"",
                        ""U_CardName"" AS ""CardName"",
                        ""U_RefId"" AS ""RefId"",
                        ""U_RefData"" AS ""RefData"",
                        ""U_Project"" AS ""Project"",
                        ""U_ProjectName"" AS ""ProjectName"",
                        ""U_SubProject"" AS ""SubProject"",
                        ""U_Activity"" AS ""Activity"",
                        ""U_ActivityId"" AS ""ActivityId"",
                        ""U_SubActivity"" AS ""SubActivity"",
                        ""U_ActivityName"" AS ""ActivityName"",
                        ""U_Date"" AS ""Date"",
                        ""U_TimeStart"" AS ""TimeStart"",
                        ""U_TimeEnd"" AS ""TimeEnd"",
                        ""U_TimePa"" AS ""TimePa"",
                        ""U_TimeNF"" AS ""TimeNF"",
                        ""U_TimeNrPa"" AS ""TimeNrPa"",
                        ""U_TimeNrNF"" AS ""TimeNrNF"",
                        ""U_TimeNrTot"" AS ""TimeNrTot"",
                        ""U_TimeNrNet"" AS ""TimeNrNet"",
                        ""U_DescExt"" AS ""DescExt"",
                        ""U_DescInt"" AS ""DescInt"",
                        ""U_Status"" AS ""Status""
                    FROM ""{_schema}"".""@SGS_PRJ_OTMS""
                    WHERE ""DocEntry"" = ?";
                
                using var command = new OdbcCommand(query, connection);
                command.Parameters.AddWithValue("@DocEntry", docEntry);
                
                using var reader = await command.ExecuteReaderAsync();
                
                if (await reader.ReadAsync())
                {
                    return new Timesheet
                    {
                        DocEntry = reader.IsDBNull(0) ? null : (int)reader.GetInt32(0),
                        Code = reader.IsDBNull(1) ? null : reader.GetString(1),
                        ResId = reader.IsDBNull(2) ? null : reader.GetString(2),
                        CardCode = reader.IsDBNull(3) ? null : reader.GetString(3),
                        CardName = reader.IsDBNull(4) ? null : reader.GetString(4),
                        RefId = reader.IsDBNull(5) ? null : reader.GetString(5),
                        RefData = reader.IsDBNull(6) ? null : reader.GetString(6),
                        Project = reader.IsDBNull(7) ? null : reader.GetString(7),
                        ProjectName = reader.IsDBNull(8) ? null : reader.GetString(8),
                        SubProject = reader.IsDBNull(9) ? null : reader.GetString(9),
                        Activity = reader.IsDBNull(10) ? null : reader.GetString(10),
                        ActivityId = reader.IsDBNull(11) ? null : reader.GetString(11),
                        SubActivity = reader.IsDBNull(12) ? null : reader.GetString(12),
                        ActivityName = reader.IsDBNull(13) ? null : reader.GetString(13),
                        Date = reader.GetDateTime(14),
                        TimeStart = reader.IsDBNull(15) ? null : (int)reader.GetInt16(15),
                        TimeEnd = reader.IsDBNull(16) ? null : (int)reader.GetInt16(16),
                        TimePa = reader.IsDBNull(17) ? null : (int)reader.GetInt16(17),
                        TimeNF = reader.IsDBNull(18) ? null : (int)reader.GetInt16(18),
                        TimeNrPa = reader.IsDBNull(19) ? null : reader.GetDecimal(19),
                        TimeNrNF = reader.IsDBNull(20) ? null : reader.GetDecimal(20),
                        TimeNrTot = reader.IsDBNull(21) ? null : reader.GetDecimal(21),
                        TimeNrNet = reader.IsDBNull(22) ? null : reader.GetDecimal(22),
                        DescExt = reader.IsDBNull(23) ? null : reader.GetString(23),
                        DescInt = reader.IsDBNull(24) ? null : reader.GetString(24),
                        Status = reader.IsDBNull(25) ? null : reader.GetString(25)
                    };
                }
                
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving timesheet with DocEntry {DocEntry} from database", docEntry);
                throw;
            }
        }

        public async Task<IEnumerable<Timesheet>> GetTimesheetsByEmployeeAsync(string employeeId)
        {
            var timesheets = new List<Timesheet>();
            
            try
            {
                using var connection = new OdbcConnection(_connectionString);
                await connection.OpenAsync();
                
                var query = $@"
                    SELECT 
                        ""DocEntry"",
                        ""Code"",
                        ""U_ResId"" AS ""ResId"",
                        ""U_CardCode"" AS ""CardCode"",
                        ""U_CardName"" AS ""CardName"",
                        ""U_RefId"" AS ""RefId"",
                        ""U_RefData"" AS ""RefData"",
                        ""U_Project"" AS ""Project"",
                        ""U_ProjectName"" AS ""ProjectName"",
                        ""U_SubProject"" AS ""SubProject"",
                        ""U_Activity"" AS ""Activity"",
                        ""U_ActivityId"" AS ""ActivityId"",
                        ""U_SubActivity"" AS ""SubActivity"",
                        ""U_ActivityName"" AS ""ActivityName"",
                        ""U_Date"" AS ""Date"",
                        ""U_TimeStart"" AS ""TimeStart"",
                        ""U_TimeEnd"" AS ""TimeEnd"",
                        ""U_TimePa"" AS ""TimePa"",
                        ""U_TimeNF"" AS ""TimeNF"",
                        ""U_TimeNrPa"" AS ""TimeNrPa"",
                        ""U_TimeNrNF"" AS ""TimeNrNF"",
                        ""U_TimeNrTot"" AS ""TimeNrTot"",
                        ""U_TimeNrNet"" AS ""TimeNrNet"",
                        ""U_DescExt"" AS ""DescExt"",
                        ""U_DescInt"" AS ""DescInt"",
                        ""U_Status"" AS ""Status""
                    FROM ""{_schema}"".""@SGS_PRJ_OTMS""
                    WHERE ""U_ResId"" = ?
                    ORDER BY ""U_Date"" DESC";
                
                using var command = new OdbcCommand(query, connection);
                command.Parameters.AddWithValue("@ResId", employeeId);
                
                using var reader = await command.ExecuteReaderAsync();
                
                while (await reader.ReadAsync())
                {
                    timesheets.Add(new Timesheet
                    {
                        DocEntry = reader.IsDBNull(0) ? null : (int)reader.GetInt32(0),
                        Code = reader.IsDBNull(1) ? null : reader.GetString(1),
                        ResId = reader.IsDBNull(2) ? null : reader.GetString(2),
                        CardCode = reader.IsDBNull(3) ? null : reader.GetString(3),
                        CardName = reader.IsDBNull(4) ? null : reader.GetString(4),
                        RefId = reader.IsDBNull(5) ? null : reader.GetString(5),
                        RefData = reader.IsDBNull(6) ? null : reader.GetString(6),
                        Project = reader.IsDBNull(7) ? null : reader.GetString(7),
                        ProjectName = reader.IsDBNull(8) ? null : reader.GetString(8),
                        SubProject = reader.IsDBNull(9) ? null : reader.GetString(9),
                        Activity = reader.IsDBNull(10) ? null : reader.GetString(10),
                        ActivityId = reader.IsDBNull(11) ? null : reader.GetString(11),
                        SubActivity = reader.IsDBNull(12) ? null : reader.GetString(12),
                        ActivityName = reader.IsDBNull(13) ? null : reader.GetString(13),
                        Date = reader.GetDateTime(14),
                        TimeStart = reader.IsDBNull(15) ? null : (int)reader.GetInt16(15),
                        TimeEnd = reader.IsDBNull(16) ? null : (int)reader.GetInt16(16),
                        TimePa = reader.IsDBNull(17) ? null : (int)reader.GetInt16(17),
                        TimeNF = reader.IsDBNull(18) ? null : (int)reader.GetInt16(18),
                        TimeNrPa = reader.IsDBNull(19) ? null : reader.GetDecimal(19),
                        TimeNrNF = reader.IsDBNull(20) ? null : reader.GetDecimal(20),
                        TimeNrTot = reader.IsDBNull(21) ? null : reader.GetDecimal(21),
                        TimeNrNet = reader.IsDBNull(22) ? null : reader.GetDecimal(22),
                        DescExt = reader.IsDBNull(23) ? null : reader.GetString(23),
                        DescInt = reader.IsDBNull(24) ? null : reader.GetString(24),
                        Status = reader.IsDBNull(25) ? null : reader.GetString(25),
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving timesheets for employee {EmployeeId} from database", employeeId);
                throw;
            }
            
            return timesheets;
        }

        public async Task<IEnumerable<Timesheet>> GetTimesheetsByProjectAsync(string projectId)
        {
            var timesheets = new List<Timesheet>();
            
            try
            {
                using var connection = new OdbcConnection(_connectionString);
                await connection.OpenAsync();
                
                var query = $@"
                    SELECT 
                        ""DocEntry"",
                        ""Code"",
                        ""U_ResId"" AS ""ResId"",
                        ""U_CardCode"" AS ""CardCode"",
                        ""U_CardName"" AS ""CardName"",
                        ""U_RefId"" AS ""RefId"",
                        ""U_RefData"" AS ""RefData"",
                        ""U_Project"" AS ""Project"",
                        ""U_ProjectName"" AS ""ProjectName"",
                        ""U_SubProject"" AS ""SubProject"",
                        ""U_Activity"" AS ""Activity"",
                        ""U_ActivityId"" AS ""ActivityId"",
                        ""U_SubActivity"" AS ""SubActivity"",
                        ""U_ActivityName"" AS ""ActivityName"",
                        ""U_Date"" AS ""Date"",
                        ""U_TimeStart"" AS ""TimeStart"",
                        ""U_TimeEnd"" AS ""TimeEnd"",
                        ""U_TimePa"" AS ""TimePa"",
                        ""U_TimeNF"" AS ""TimeNF"",
                        ""U_TimeNrPa"" AS ""TimeNrPa"",
                        ""U_TimeNrNF"" AS ""TimeNrNF"",
                        ""U_TimeNrTot"" AS ""TimeNrTot"",
                        ""U_TimeNrNet"" AS ""TimeNrNet"",
                        ""U_DescExt"" AS ""DescExt"",
                        ""U_DescInt"" AS ""DescInt"",
                        ""U_Status"" AS ""Status""
                    FROM ""{_schema}"".""@SGS_PRJ_OTMS""
                    WHERE ""U_Project"" = ?
                    ORDER BY ""U_Date"" DESC";
                
                using var command = new OdbcCommand(query, connection);
                command.Parameters.AddWithValue("@Project", projectId);
                
                using var reader = await command.ExecuteReaderAsync();
                
                while (await reader.ReadAsync())
                {
                    timesheets.Add(new Timesheet
                    {
                        DocEntry = reader.IsDBNull(0) ? null : (int)reader.GetInt32(0),
                        Code = reader.IsDBNull(1) ? null : reader.GetString(1),
                        ResId = reader.IsDBNull(2) ? null : reader.GetString(2),
                        CardCode = reader.IsDBNull(3) ? null : reader.GetString(3),
                        CardName = reader.IsDBNull(4) ? null : reader.GetString(4),
                        RefId = reader.IsDBNull(5) ? null : reader.GetString(5),
                        RefData = reader.IsDBNull(6) ? null : reader.GetString(6),
                        Project = reader.IsDBNull(7) ? null : reader.GetString(7),
                        ProjectName = reader.IsDBNull(8) ? null : reader.GetString(8),
                        SubProject = reader.IsDBNull(9) ? null : reader.GetString(9),
                        Activity = reader.IsDBNull(10) ? null : reader.GetString(10),
                        ActivityId = reader.IsDBNull(11) ? null : reader.GetString(11),
                        SubActivity = reader.IsDBNull(12) ? null : reader.GetString(12),
                        ActivityName = reader.IsDBNull(13) ? null : reader.GetString(13),
                        Date = reader.GetDateTime(14),
                        TimeStart = reader.IsDBNull(15) ? null : (int)reader.GetInt16(15),
                        TimeEnd = reader.IsDBNull(16) ? null : (int)reader.GetInt16(16),
                        TimePa = reader.IsDBNull(17) ? null : (int)reader.GetInt16(17),
                        TimeNF = reader.IsDBNull(18) ? null : (int)reader.GetInt16(18),
                        TimeNrPa = reader.IsDBNull(19) ? null : reader.GetDecimal(19),
                        TimeNrNF = reader.IsDBNull(20) ? null : reader.GetDecimal(20),
                        TimeNrTot = reader.IsDBNull(21) ? null : reader.GetDecimal(21),
                        TimeNrNet = reader.IsDBNull(22) ? null : reader.GetDecimal(22),
                        DescExt = reader.IsDBNull(23) ? null : reader.GetString(23),
                        DescInt = reader.IsDBNull(24) ? null : reader.GetString(24),
                        Status = reader.IsDBNull(25) ? null : reader.GetString(25),
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving timesheets for project {ProjectId} from database", projectId);
                throw;
            }
            
            return timesheets;
        }

        public async Task<IEnumerable<Timesheet>> GetTimesheetsByDateRangeAsync(DateTime startDate, DateTime endDate)
        {
            var timesheets = new List<Timesheet>();
            
            try
            {
                using var connection = new OdbcConnection(_connectionString);
                await connection.OpenAsync();
                
                var query = $@"
                    SELECT 
                        ""DocEntry"",
                        ""Code"",
                        ""U_ResId"" AS ""ResId"",
                        ""U_CardCode"" AS ""CardCode"",
                        ""U_CardName"" AS ""CardName"",
                        ""U_RefId"" AS ""RefId"",
                        ""U_RefData"" AS ""RefData"",
                        ""U_Project"" AS ""Project"",
                        ""U_ProjectName"" AS ""ProjectName"",
                        ""U_SubProject"" AS ""SubProject"",
                        ""U_Activity"" AS ""Activity"",
                        ""U_ActivityId"" AS ""ActivityId"",
                        ""U_SubActivity"" AS ""SubActivity"",
                        ""U_ActivityName"" AS ""ActivityName"",
                        ""U_Date"" AS ""Date"",
                        ""U_TimeStart"" AS ""TimeStart"",
                        ""U_TimeEnd"" AS ""TimeEnd"",
                        ""U_TimePa"" AS ""TimePa"",
                        ""U_TimeNF"" AS ""TimeNF"",
                        ""U_TimeNrPa"" AS ""TimeNrPa"",
                        ""U_TimeNrNF"" AS ""TimeNrNF"",
                        ""U_TimeNrTot"" AS ""TimeNrTot"",
                        ""U_TimeNrNet"" AS ""TimeNrNet"",
                        ""U_DescExt"" AS ""DescExt"",
                        ""U_DescInt"" AS ""DescInt"",
                        ""U_Status"" AS ""Status""
                    FROM ""{_schema}"".""@SGS_PRJ_OTMS""
                    WHERE ""U_Date"" BETWEEN ? AND ?
                    ORDER BY ""U_Date"" DESC";
                
                using var command = new OdbcCommand(query, connection);
                command.Parameters.AddWithValue("@StartDate", startDate);
                command.Parameters.AddWithValue("@EndDate", endDate);
                
                using var reader = await command.ExecuteReaderAsync();
                
                while (await reader.ReadAsync())
                {
                    timesheets.Add(new Timesheet
                    {
                        DocEntry = reader.IsDBNull(0) ? null : (int)reader.GetInt32(0),
                        Code = reader.IsDBNull(1) ? null : reader.GetString(1),
                        ResId = reader.IsDBNull(2) ? null : reader.GetString(2),
                        CardCode = reader.IsDBNull(3) ? null : reader.GetString(3),
                        CardName = reader.IsDBNull(4) ? null : reader.GetString(4),
                        RefId = reader.IsDBNull(5) ? null : reader.GetString(5),
                        RefData = reader.IsDBNull(6) ? null : reader.GetString(6),
                        Project = reader.IsDBNull(7) ? null : reader.GetString(7),
                        ProjectName = reader.IsDBNull(8) ? null : reader.GetString(8),
                        SubProject = reader.IsDBNull(9) ? null : reader.GetString(9),
                        Activity = reader.IsDBNull(10) ? null : reader.GetString(10),
                        ActivityId = reader.IsDBNull(11) ? null : reader.GetString(11),
                        SubActivity = reader.IsDBNull(12) ? null : reader.GetString(12),
                        ActivityName = reader.IsDBNull(13) ? null : reader.GetString(13),
                        Date = reader.GetDateTime(14),
                        TimeStart = reader.IsDBNull(15) ? null : (int)reader.GetInt32(15),
                        TimeEnd = reader.IsDBNull(16) ? null : (int)reader.GetInt32(16),
                        TimePa = reader.IsDBNull(17) ? null : (int)reader.GetInt32(17),
                        TimeNF = reader.IsDBNull(18) ? null : (int)reader.GetInt32(18),
                        TimeNrPa = reader.IsDBNull(19) ? null : reader.GetDecimal(19),
                        TimeNrNF = reader.IsDBNull(20) ? null : reader.GetDecimal(20),
                        TimeNrTot = reader.IsDBNull(21) ? null : reader.GetDecimal(21),
                        TimeNrNet = reader.IsDBNull(22) ? null : reader.GetDecimal(22),
                        DescExt = reader.IsDBNull(23) ? null : reader.GetString(23),
                        DescInt = reader.IsDBNull(24) ? null : reader.GetString(24),
                        Status = reader.IsDBNull(25) ? null : reader.GetString(25),
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving timesheets for date range {StartDate} to {EndDate} from database", startDate, endDate);
                throw;
            }
            
            return timesheets;
        }

        public async Task<IEnumerable<Timesheet>> GetTimesheetsByEmployeeAndDateRangeAsync(string employeeId, DateTime startDate, DateTime endDate)
        {
            var timesheets = new List<Timesheet>();
            
            try
            {
                using var connection = new OdbcConnection(_connectionString);
                await connection.OpenAsync();
                
                var query = $@"
                    SELECT 
                        ""DocEntry"",
                        ""Code"",
                        ""U_ResId"" AS ""ResId"",
                        ""U_CardCode"" AS ""CardCode"",
                        ""U_CardName"" AS ""CardName"",
                        ""U_RefId"" AS ""RefId"",
                        ""U_RefData"" AS ""RefData"",
                        ""U_Project"" AS ""Project"",
                        ""U_ProjectName"" AS ""ProjectName"",
                        ""U_SubProject"" AS ""SubProject"",
                        ""U_Activity"" AS ""Activity"",
                        ""U_ActivityId"" AS ""ActivityId"",
                        ""U_SubActivity"" AS ""SubActivity"",
                        ""U_ActivityName"" AS ""ActivityName"",
                        ""U_Date"" AS ""Date"",
                        ""U_TimeStart"" AS ""TimeStart"",
                        ""U_TimeEnd"" AS ""TimeEnd"",
                        ""U_TimePa"" AS ""TimePa"",
                        ""U_TimeNF"" AS ""TimeNF"",
                        ""U_TimeNrPa"" AS ""TimeNrPa"",
                        ""U_TimeNrNF"" AS ""TimeNrNF"",
                        ""U_TimeNrTot"" AS ""TimeNrTot"",
                        ""U_TimeNrNet"" AS ""TimeNrNet"",
                        ""U_DescExt"" AS ""DescExt"",
                        ""U_DescInt"" AS ""DescInt"",
                        ""U_Status"" AS ""Status""
                    FROM ""{_schema}"".""@SGS_PRJ_OTMS""
                    WHERE ""U_ResId"" = ? AND ""U_Date"" BETWEEN ? AND ?
                    ORDER BY ""U_Date"" DESC";
                
                using var command = new OdbcCommand(query, connection);
                command.Parameters.AddWithValue("@ResId", employeeId);
                command.Parameters.AddWithValue("@StartDate", startDate);
                command.Parameters.AddWithValue("@EndDate", endDate);
                
                using var reader = await command.ExecuteReaderAsync();
                
                while (await reader.ReadAsync())
                {
                    timesheets.Add(new Timesheet
                    {
                        DocEntry = reader.IsDBNull(0) ? null : (int)reader.GetInt32(0),
                        Code = reader.IsDBNull(1) ? null : reader.GetString(1),
                        ResId = reader.IsDBNull(2) ? null : reader.GetString(2),
                        CardCode = reader.IsDBNull(3) ? null : reader.GetString(3),
                        CardName = reader.IsDBNull(4) ? null : reader.GetString(4),
                        RefId = reader.IsDBNull(5) ? null : reader.GetString(5),
                        RefData = reader.IsDBNull(6) ? null : reader.GetString(6),
                        Project = reader.IsDBNull(7) ? null : reader.GetString(7),
                        ProjectName = reader.IsDBNull(8) ? null : reader.GetString(8),
                        SubProject = reader.IsDBNull(9) ? null : reader.GetString(9),
                        Activity = reader.IsDBNull(10) ? null : reader.GetString(10),
                        ActivityId = reader.IsDBNull(11) ? null : reader.GetString(11),
                        SubActivity = reader.IsDBNull(12) ? null : reader.GetString(12),
                        ActivityName = reader.IsDBNull(13) ? null : reader.GetString(13),
                        Date = reader.GetDateTime(14),
                        TimeStart = reader.IsDBNull(15) ? null : (int)reader.GetInt16(15),
                        TimeEnd = reader.IsDBNull(16) ? null : (int)reader.GetInt16(16),
                        TimePa = reader.IsDBNull(17) ? null : (int)reader.GetInt16(17),
                        TimeNF = reader.IsDBNull(18) ? null : (int)reader.GetInt16(18),
                        TimeNrPa = reader.IsDBNull(19) ? null : reader.GetDecimal(19),
                        TimeNrNF = reader.IsDBNull(20) ? null : reader.GetDecimal(20),
                        TimeNrTot = reader.IsDBNull(21) ? null : reader.GetDecimal(21),
                        TimeNrNet = reader.IsDBNull(22) ? null : reader.GetDecimal(22),
                        DescExt = reader.IsDBNull(23) ? null : reader.GetString(23),
                        DescInt = reader.IsDBNull(24) ? null : reader.GetString(24),
                        Status = reader.IsDBNull(25) ? null : reader.GetString(25),
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving timesheets for employee {EmployeeId} in date range {StartDate} to {EndDate} from database", employeeId, startDate, endDate);
                throw;
            }
            
            return timesheets;
        }

        public async Task<string> GetNextTimesheetCodeAsync()
        {
            try
            {
                using var connection = new OdbcConnection(_connectionString);
                await connection.OpenAsync();
                
                // Query to get the maximum Code value converted to numeric, then add 1
                var query = $@"
                    SELECT 
                        CASE 
                            WHEN MAX(CAST(""Code"" AS INTEGER)) IS NULL THEN 1
                            ELSE MAX(CAST(""Code"" AS INTEGER)) + 1
                        END
                    FROM ""{_schema}"".""@SGS_PRJ_OTMS""
                    WHERE ""Code"" IS NOT NULL 
                    AND ""Code"" != ''";
                
                using var command = new OdbcCommand(query, connection);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var result = await command.ExecuteScalarAsync();
                sw.Stop();
                _logger.LogInformation("ODBC ExecuteScalar completed in {ElapsedMs} ms", sw.ElapsedMilliseconds);
                
                if (result != null && result != DBNull.Value)
                {
                    return result.ToString()!;
                }
                
                return "1"; // Default to 1 if no records found
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting next timesheet code from database");
                throw;
            }
        }

        /// <summary>
        /// Stato di fatturazione delle righe di timesheet nella finestra [from, to] (per <c>U_Date</c>), con
        /// la fattura che le porta quando <c>U_DestType = '13'</c>. Sola lettura: nessuna scrittura verso SAP.
        /// <para>
        /// Misurato il 16/09/2026 su dati di produzione: <c>U_Status</c> prende esattamente tre valori —
        /// «Fatturato» (1.351 righe), «Confermato» (5.619), «Inserito» (49). Delle righe fatturate, 1.271
        /// puntano a una fattura (<c>U_DestType '13'</c>), 75 a un ordine (<c>'17'</c>) e 5 a una consegna
        /// (<c>'15'</c>): una riga può quindi risultare fatturata <b>senza</b> numero di fattura, e il join
        /// con <c>OINV</c> resta volutamente un arricchimento facoltativo. <c>U_DestType</c> è alfanumerico
        /// su questo impianto: si confronta con la stringa <c>'13'</c>, mai con un intero. <c>U_TimeNrNet</c>
        /// è testo su alcune righe: si legge con <see cref="ParseHours"/>, mai con <c>GetDecimal</c> (che
        /// fallirebbe sulle righe testuali) né con <c>Convert.ToDecimal</c> (che userebbe la cultura del
        /// server).
        /// </para>
        /// <para>
        /// La chiave restituita è <c>DocEntry</c>, <b>mai</b> <c>Code</c>: è quella che il portale riceve alla
        /// creazione della riga e conserva. Le due colonne divergono su 826 righe di 7.019 (misurato il
        /// 16/09/2026), con uno scarto costante sulle più recenti, quindi rispondere con <c>Code</c>
        /// aggancerebbe al portale lo stato di un'altra riga di timesheet.
        /// </para>
        /// <para>
        /// La risposta porta anche <c>U_ResId</c>/<c>U_Project</c>/<c>U_Activity</c>/<c>U_Date</c> come
        /// <see cref="TimesheetBillingState.ErpResourceCode"/>/<see cref="TimesheetBillingState.ErpProjectCode"/>/
        /// <see cref="TimesheetBillingState.ErpActivityCode"/>/<see cref="TimesheetBillingState.WorkedOn"/>: è la
        /// chiave di riserva per l'abbinamento per attributi delle 414 righe del portale senza <c>ErpDocId</c>
        /// (misurato il 16/09/2026 — 120 mai spinte a SAP, 293 marcate "da non esportare" perché inserite a
        /// mano in SAP). Colonne aggiunte in coda alla SELECT apposta per non spostare gli ordinali già in
        /// uso sopra. Lette con la stessa prudenza di <c>U_TimeNrNet</c> — mai <c>GetInt32</c>/<c>GetDecimal</c>
        /// diretto, che su questo impianto possono fallire perché la colonna arriva come testo — vedi
        /// <see cref="ParseErpText"/>. <c>U_Activity</c> viene trasportato SENZA normalizzare lo zero-padding
        /// (SAP "7" vs portale "07", vedi <see cref="TimesheetBillingState.ErpActivityCode"/>): il confronto
        /// tollerante è compito del portale.
        /// </para>
        /// </summary>
        public async Task<IEnumerable<TimesheetBillingState>> GetTimesheetBillingStatesAsync(DateTime from, DateTime to)
        {
            var result = new List<TimesheetBillingState>();

            try
            {
                using var connection = await CreateOpenConnectionAsync();

                var query = $@"
                    SELECT T.""DocEntry"", T.""U_Status"", T.""U_DestType"", T.""U_DestEntry"", T.""U_TimeNrNet"",
                           H.""DocNum"", H.""DocDate"",
                           T.""U_ResId"", T.""U_Project"", T.""U_Activity"", T.""U_Date""
                    FROM ""{_schema}"".""@SGS_PRJ_OTMS"" T
                    LEFT JOIN ""{_schema}"".""OINV"" H ON T.""U_DestType"" = '13' AND H.""DocEntry"" = T.""U_DestEntry""
                    WHERE T.""Canceled"" = 'N' AND T.""U_Date"" >= ? AND T.""U_Date"" <= ?
                    ORDER BY T.""DocEntry""";

                using var command = new OdbcCommand(query, connection);
                command.Parameters.AddWithValue("@From", from.Date);
                command.Parameters.AddWithValue("@To", to.Date);

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var status = reader.IsDBNull(1) ? null : reader.GetString(1);
                    var destType = reader.IsDBNull(2) ? null : reader.GetString(2);

                    result.Add(new TimesheetBillingState
                    {
                        // DocEntry è numerico in SAP: si trasporta come stringa perché il contratto verso il
                        // portale è ERP-neutro (un altro gestionale può avere un identificativo non numerico).
                        ErpDocId = reader.IsDBNull(0) ? string.Empty : Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture) ?? string.Empty,
                        State = MapBillingState(status, destType),
                        InvoiceErpDocNumber = reader.IsDBNull(5) ? null : Convert.ToString(reader.GetValue(5)),
                        InvoicedOn = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                        Hours = ParseHours(reader.IsDBNull(4) ? null : reader.GetValue(4)),
                        // Ordinali 7-10: chiave di riserva per l'abbinamento per attributi, vedi doc del metodo.
                        ErpResourceCode = ParseErpText(reader.IsDBNull(7) ? null : reader.GetValue(7)),
                        ErpProjectCode = ParseErpText(reader.IsDBNull(8) ? null : reader.GetValue(8)),
                        ErpActivityCode = ParseErpText(reader.IsDBNull(9) ? null : reader.GetValue(9)),
                        WorkedOn = ParseWorkedOn(reader.IsDBNull(10) ? null : reader.GetValue(10)),
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving timesheet billing states for date range {From} to {To} from database", from, to);
                throw;
            }

            return result;
        }

        /// <summary>
        /// Traduce <c>@SGS_PRJ_OTMS.U_Status</c> nel contratto ERP-neutro. «Fatturato» vince sempre, anche
        /// senza <c>U_DestType</c> valorizzato: lo stato SAP è la fonte di verità, il documento di
        /// destinazione è solo un arricchimento che può mancare senza far retrocedere la riga a bozza.
        /// Qualunque stato non riconosciuto — incluso null — diventa "draft": è la lettura prudente quando
        /// la provenienza non è chiara.
        /// </summary>
        internal static string MapBillingState(string? status, string? destType) => status switch
        {
            "Fatturato" => "invoiced",
            "Confermato" => "confirmed",
            "Inserito" => "draft",
            _ => "draft",
        };

        /// <summary>
        /// Legge <c>@SGS_PRJ_OTMS.U_TimeNrNet</c>, che su alcune righe arriva come testo invece che come numero.
        /// Le ore non passano mai da <c>Convert.ToDecimal</c>: quello userebbe la cultura del server, dove un
        /// "2.5" diventerebbe 25 in silenzio, e solleverebbe un'eccezione su stringa vuota facendo cadere
        /// l'intera finestra di sincronizzazione per colpa di una riga sola. Un valore illeggibile vale zero ore.
        /// </summary>
        internal static decimal ParseHours(object? raw) => raw switch
        {
            null or DBNull => 0m,
            decimal d => d,
            double dbl => (decimal)dbl,
            float f => (decimal)f,
            int i => i,
            long l => l,
            short s => s,
            _ => decimal.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture)?.Trim(),
                    NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
                 ? parsed
                 : 0m,
        };

        /// <summary>
        /// Legge una colonna testuale di <c>@SGS_PRJ_OTMS</c> usata come chiave di riserva (<c>U_ResId</c>,
        /// <c>U_Project</c>, <c>U_Activity</c>): su questo impianto una colonna concettualmente stringa può
        /// arrivare dal driver ODBC tipizzata numerica (è già successo con <c>U_TimeNrNet</c>, vedi
        /// <see cref="ParseHours"/>), quindi mai <c>GetInt32</c>/<c>GetDecimal</c> diretto sull'ordinale. La
        /// conversione a stringa è sempre a cultura invariante: un identificativo trasportato non deve
        /// cambiare forma in base alla cultura del server.
        /// </summary>
        /// <summary>
        /// I valori tornano ripuliti degli spazi ai bordi: le colonne di SAP sono spesso a lunghezza fissa e
        /// restituiscono la coda riempita di spazi. Il portale confronta questi codici per uguaglianza —
        /// risorsa, progetto e attività sono la chiave di riserva per le righe che non ha mai spinto — e uno
        /// spazio invisibile in coda non farebbe fallire niente in modo rumoroso: farebbe semplicemente
        /// abbinare zero righe, cioè sembrerebbe che la funzione non serva a nulla.
        /// Una stringa vuota o di soli spazi vale null: è un'assenza, non un codice.
        /// </summary>
        internal static string? ParseErpText(object? raw)
        {
            var text = raw switch
            {
                null or DBNull => null,
                string s => s,
                _ => Convert.ToString(raw, CultureInfo.InvariantCulture),
            };
            text = text?.Trim();
            return string.IsNullOrEmpty(text) ? null : text;
        }

        /// <summary>
        /// Legge <c>@SGS_PRJ_OTMS.U_Date</c> per la chiave di riserva dell'abbinamento per attributi, con la
        /// stessa prudenza delle altre colonne di questa query: se il driver la restituisce già come
        /// <see cref="DateTime"/> la usa, altrimenti prova un parsing a cultura invariante. Un valore
        /// illeggibile vale "data assente" (non fa fallire l'intera finestra per una riga sola).
        /// </summary>
        internal static DateTime? ParseWorkedOn(object? raw) => raw switch
        {
            null or DBNull => null,
            DateTime dt => dt,
            _ => DateTime.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var parsed)
                 ? parsed
                 : null,
        };

        public async Task<IEnumerable<CustomerSummary>> GetCustomersAsync()
        {
            var customers = new List<CustomerSummary>();
            try
            {
                using var connection = await CreateOpenConnectionAsync();

                // Anagrafica cliente estesa (partita IVA, codice fiscale, indirizzo, email) per il portale AX.
                // LicTradNum = P.IVA/Tax ID; AddID = campo libero spesso usato per il Codice Fiscale
                // (nello schema HANA la colonna è "AddID", non "AdditionalID");
                // Address = indirizzo di fatturazione predefinito; E_Mail = email anagrafica.
                var query = $@"
                    SELECT
                        ""CardCode"",
                        ""CardName"",
                        ""LicTradNum"",
                        ""AddID"",
                        ""Address"",
                        ""E_Mail""
                    FROM ""{_schema}"".""OCRD""
                    WHERE ""CardType"" = 'C'
                    ORDER BY ""CardName""";

                using var command = new OdbcCommand(query, connection);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                using var reader = await command.ExecuteReaderAsync();
                sw.Stop();
                _logger.LogInformation("ODBC ExecuteReader(customers) completed in {ElapsedMs} ms", sw.ElapsedMilliseconds);
                while (await reader.ReadAsync())
                {
                    customers.Add(new CustomerSummary
                    {
                        CardCode = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                        CardName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                        VatNumber = reader.IsDBNull(2) ? null : reader.GetString(2),
                        TaxCode = reader.IsDBNull(3) ? null : reader.GetString(3),
                        Address = reader.IsDBNull(4) ? null : reader.GetString(4),
                        Email = reader.IsDBNull(5) ? null : reader.GetString(5)
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving customers from database");
                throw;
            }
            return customers;
        }

        // Query parametrica del profilo cliente esteso (ERP-neutro). Read-only.
        // Le colonne oltre CardCode/CardName sono best-effort: LEFT JOIN così un cliente
        // senza termini/agente/gruppo non viene escluso. Da validare sullo schema SAP del tenant.
        private string BuildCustomerProfileQuery(bool single) => $@"
            SELECT
                T0.""CardCode"",
                T0.""CardName"",
                T0.""Phone1"",
                T0.""E_Mail"",
                T1.""PymntGroup"",
                T2.""Descript"",
                T3.""SlpName"",
                T4.""GroupName"",
                T0.""CreateDate"",
                T0.""CreditLine"",
                T0.""Balance"",
                T0.""DflIBAN"",
                T0.""IntrntSite""
            FROM ""{_schema}"".""OCRD"" T0
            LEFT JOIN ""{_schema}"".""OCTG"" T1 ON T1.""GroupNum"" = T0.""GroupNum""
            LEFT JOIN ""{_schema}"".""OPYM"" T2 ON T2.""PayMethCod"" = T0.""PymCode""
            LEFT JOIN ""{_schema}"".""OSLP"" T3 ON T3.""SlpCode"" = T0.""SlpCode""
            LEFT JOIN ""{_schema}"".""OCRG"" T4 ON T4.""GroupCode"" = T0.""GroupCode""
            WHERE T0.""CardType"" = 'C'" + (single ? @"
              AND T0.""CardCode"" = ?" : @"
            ORDER BY T0.""CardName""");

        private static CustomerProfile MapCustomerProfile(System.Data.Common.DbDataReader reader) => new()
        {
            CardCode = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
            CardName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
            Phone = reader.IsDBNull(2) ? null : reader.GetString(2),
            Email = reader.IsDBNull(3) ? null : reader.GetString(3),
            PaymentTermsLabel = reader.IsDBNull(4) ? null : reader.GetString(4),
            PaymentMethod = reader.IsDBNull(5) ? null : reader.GetString(5),
            SalesAgent = reader.IsDBNull(6) ? null : reader.GetString(6),
            BusinessPartnerGroup = reader.IsDBNull(7) ? null : reader.GetString(7),
            CustomerSince = reader.IsDBNull(8) ? null : Convert.ToDateTime(reader.GetValue(8)),
            CreditLimit = reader.IsDBNull(9) ? null : Convert.ToDecimal(reader.GetValue(9)),
            CurrentBalance = reader.IsDBNull(10) ? null : Convert.ToDecimal(reader.GetValue(10)),
            // OCRD."DflIBAN" = IBAN del conto bancario di default del cliente; "IntrntSite" = sito web.
            Iban = reader.IsDBNull(11) ? null : reader.GetString(11),
            Website = reader.IsDBNull(12) ? null : reader.GetString(12),
        };

        public async Task<IEnumerable<CustomerProfile>> GetCustomerProfilesAsync()
        {
            var profiles = new List<CustomerProfile>();
            try
            {
                using var connection = await CreateOpenConnectionAsync();
                using var command = new OdbcCommand(BuildCustomerProfileQuery(single: false), connection);
                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    profiles.Add(MapCustomerProfile(reader));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving customer profiles from database");
                throw;
            }
            return profiles;
        }

        public async Task<CustomerProfile?> GetCustomerProfileAsync(string cardCode)
        {
            try
            {
                using var connection = await CreateOpenConnectionAsync();
                using var command = new OdbcCommand(BuildCustomerProfileQuery(single: true), connection);
                command.Parameters.AddWithValue("@CardCode", cardCode);
                using var reader = await command.ExecuteReaderAsync();
                return await reader.ReadAsync() ? MapCustomerProfile(reader) : null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving customer profile {CardCode} from database", cardCode);
                throw;
            }
        }

        public async Task<IEnumerable<ContactSummary>> GetContactsByCustomerAsync(string cardCode)
        {
            var contacts = new List<ContactSummary>();
            try
            {
                using var connection = await CreateOpenConnectionAsync();

                var query = $@"
                    SELECT 
                        ""CntctCode"" AS ""Code"",
                        ""Name""
                    FROM ""{_schema}"".""OCPR""
                    WHERE ""CardCode"" = ?
                    ORDER BY ""Name""";

                using var command = new OdbcCommand(query, connection);
                command.Parameters.AddWithValue("@CardCode", cardCode);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                using var reader = await command.ExecuteReaderAsync();
                sw.Stop();
                _logger.LogInformation("ODBC ExecuteReader(contacts) completed in {ElapsedMs} ms", sw.ElapsedMilliseconds);
                while (await reader.ReadAsync())
                {
                    contacts.Add(new ContactSummary
                    {
                        Code = reader.IsDBNull(0) ? string.Empty : reader.GetInt32(0).ToString(),
                        Name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1)
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving contacts for customer {CardCode} from database", cardCode);
                throw;
            }
            return contacts;
        }

        public async Task<IEnumerable<ProjectSummary>> GetProjectsAsync()
        {
            var projects = new List<ProjectSummary>();
            try
            {
                using var connection = new OdbcConnection(_connectionString);
                await connection.OpenAsync();

                // Includo i dati cliente del progetto (CardCode/CardName) richiesti dal portale AX (ErpProjectDto).
                // FiscalProjectCode: vedi FiscalProjectCodeExpression per il perché del COALESCE+NULLIF.
                var query = $@"
                    SELECT
                        T.""AbsEntry"" AS ""Code"",
                        T.""NAME"" AS ""Name"",
                        T.""CARDCODE"" AS ""CardCode"",
                        C.""CardName"" AS ""CardName"",
                        {FiscalProjectCodeExpression} AS ""FiscalProjectCode""
                    FROM ""{_schema}"".""OPMG"" T
                    LEFT JOIN ""{_schema}"".""OCRD"" C ON C.""CardCode"" = T.""CARDCODE""
                    ORDER BY T.""NAME""";

                using var command = new OdbcCommand(query, connection);
                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    projects.Add(new ProjectSummary
                    {
                        Code = reader.IsDBNull(0) ? string.Empty : reader.GetInt32(0).ToString(),
                        Name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                        CardCode = reader.IsDBNull(2) ? null : reader.GetString(2),
                        CardName = reader.IsDBNull(3) ? null : reader.GetString(3),
                        FiscalProjectCode = NullIfEmpty(reader, 4)
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving projects from database");
                throw;
            }
            return projects;
        }

        public async Task<ProjectLookupDetail?> GetProjectLookupDetailByCodeAsync(string projectCode)
        {
            try
            {
                using var connection = await CreateOpenConnectionAsync();

                var query = $@"
                    SELECT
                        T.""AbsEntry"" AS ""Code"",
                        T.""NAME"" AS ""Name"",
                        T.""CARDCODE"" AS ""CardCode"",
                        C.""CardName"" AS ""CardName""
                    FROM ""{_schema}"".""OPMG"" T
                    LEFT JOIN ""{_schema}"".""OCRD"" C ON C.""CardCode"" = T.""CARDCODE""
                    WHERE T.""AbsEntry"" = ?";

                using var command = new OdbcCommand(query, connection);
                command.Parameters.AddWithValue("@ProjectCode", projectCode);
                using var reader = await command.ExecuteReaderAsync();

                if (!await reader.ReadAsync())
                {
                    return null;
                }

                return new ProjectLookupDetail
                {
                    Code = reader.IsDBNull(0) ? string.Empty : reader.GetInt32(0).ToString(),
                    Name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    CardCode = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    CardName = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving project detail for project {ProjectCode} from database", projectCode);
                throw;
            }
        }

        public async Task<IEnumerable<ActivitySummary>> GetActivitiesByProjectAsync(string projectCode)
        {
            var activities = new List<ActivitySummary>();
            try
            {
                using var connection = new OdbcConnection(_connectionString);
                await connection.OpenAsync();

                var query = $@"
                    SELECT 
                        ""LineID"" AS ""Code"",
                        ""DSCRIPTION"" AS ""Name"",
                        ""U_SGS_PRJ_UnitMsr"",
                        ""U_SGS_PRJ_Price""
                    FROM ""{_schema}"".""PMG1""
                    WHERE ""AbsEntry"" = ?
                    ORDER BY ""LineID""";

                using var command = new OdbcCommand(query, connection);
                command.Parameters.AddWithValue("@Project", projectCode);
                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    activities.Add(new ActivitySummary
                    {
                        Code = reader.IsDBNull(0) ? string.Empty : reader.GetInt32(0).ToString("D2"),
                        Name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                        UoM = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                        Price = reader.IsDBNull(3) ? 0 : reader.GetDecimal(3),
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving activities for project {ProjectCode} from database", projectCode);
                throw;
            }
            return activities;
        }

        public async Task<IEnumerable<ProjectSummary>> GetProjectsByCustomerAsync(string cardCode)
        {
            var projects = new List<ProjectSummary>();
            try
            {
                using var connection = new OdbcConnection(_connectionString);
                await connection.OpenAsync();

                // Projects linked to a BP via SAP B1 standard tables: OINV/RDR/OPRJ linkage varies by implementation.
                // Here we leverage the timesheet source table if projects are referenced there by CardCode, else fallback to OPRJ + OCRD link via custom relations.
                // FiscalProjectCode: vedi FiscalProjectCodeExpression per il perché del COALESCE+NULLIF.
                var query = $@"
                    SELECT
                        T.""AbsEntry"" AS ""Code"",
                        T.""NAME"" AS ""Name"",
                        {FiscalProjectCodeExpression} AS ""FiscalProjectCode""
                    FROM ""{_schema}"".""OPMG"" T
                    WHERE T.""CARDCODE"" = ?
                    ORDER BY T.""NAME""";

                using var command = new OdbcCommand(query, connection);
                command.Parameters.AddWithValue("@CardCode", cardCode);
                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    projects.Add(new ProjectSummary
                    {
                        Code = reader.IsDBNull(0) ? string.Empty : reader.GetInt32(0).ToString(),
                        Name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                        FiscalProjectCode = NullIfEmpty(reader, 2)
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving projects for customer {CardCode} from database", cardCode);
                throw;
            }
            return projects;
        }

        public async Task<IEnumerable<ResourceSummary>> GetResourcesAsync()
        {
            var resources = new List<ResourceSummary>();
            try
            {
                using var connection = new OdbcConnection(_connectionString);
                await connection.OpenAsync();

                // Read from common JWT claims to handle mapping differences
                var principal = _httpContextAccessor.HttpContext?.User;
                var jti = principal?.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;

                // Chiamante machine-to-machine (header X-Api-Key, es. il portale AX.360): il principal non
                // rappresenta un utente SAP, quindi il suo nome ("ax360-erp") non e' un OUSR.USER_CODE e
                // filtrare su di esso restituirebbe SEMPRE zero risorse. Va trattato come "nessun utente",
                // cioe' elenco completo delle risorse attive: e' il consumer che sceglie a chi abbinarle.
                var isMachineClient = string.Equals(
                    principal?.FindFirst("client_type")?.Value, "api_key", StringComparison.OrdinalIgnoreCase);

                var userName = isMachineClient
                    ? null
                    : (principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                       ?? principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                       ?? principal?.Claims.FirstOrDefault(c => c.Type == "preferred_username")?.Value
                       ?? principal?.Claims.FirstOrDefault(c => c.Type == "unique_name")?.Value
                       ?? principal?.FindFirst(ClaimTypes.Name)?.Value);
                _logger.LogDebug("GetResourcesAsync principal resolved: sub/name={User}, jti={Jti}, machineClient={Machine}", userName ?? "", jti ?? "", isMachineClient);
                var query = $@"
                    SELECT DISTINCT
                        T0.""ResCode"" AS ""Code"",
                        T0.""ResName"" AS ""Name""
                    FROM ""{_schema}"".""ORSC"" T0
                    INNER JOIN ""{_schema}"".""RSC4"" T1 ON T0.""ResCode"" = T1.""ResCode""
                    INNER JOIN ""{_schema}"".""OHEM"" T2 ON T1.""EmpID"" = T2.""empID""
                    INNER JOIN ""{_schema}"".""OUSR"" T3 ON T2.""userId"" = T3.""USERID""
                    LEFT JOIN
                    (
                        ""{_schema}"".""OHEM"" T4
                        INNER JOIN ""{_schema}"".""OUSR"" T5 ON T4.""userId"" = T5.""USERID""
                    ) ON T4.""empID"" = T2.""manager""
                    WHERE T0.""validFor"" = 'Y' AND T2.""Active"" = 'Y'";

                using var command = new OdbcCommand();
                command.Connection = connection;
                if (string.IsNullOrWhiteSpace(userName))
                {
                    // Client M2M, auth disabilitata o token non disponibile: tutte le risorse attive.
                    _logger.LogInformation(
                        "GetResourcesAsync: nessun utente SAP nel contesto (machineClient={Machine}), restituisco tutte le risorse attive",
                        isMachineClient);
                    command.CommandText = query + @" ORDER BY T0.""ResName""";
                }
                else
                {
                    command.CommandText = query + @"
                        AND (T3.""USER_CODE"" = ? OR IFNULL(T5.""USER_CODE"",'') = ?)
                        ORDER BY T0.""ResName""";
                    command.Parameters.AddWithValue("@User1", userName);
                    command.Parameters.AddWithValue("@User2", userName);
                }

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var code = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                    var name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                    resources.Add(new ResourceSummary { Code = code, Name = name });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving resources from database");
                throw;
            }
            return resources;
        }

        public async Task<ActivityTimeTotal?> GetActivityTimeTotAsync(string projectId, string activityId)
        {
            try
            {
                using var connection = new OdbcConnection(_connectionString);
                await connection.OpenAsync();

                var query = $@"
                    SELECT 
                        T0.""U_Project"", 
                        T0.""U_ActivityId"", 
                        SUM(T0.""U_TimeNrTot"") AS ""TimeTot""
                    FROM ""{_schema}"".""@SGS_PRJ_OTMS"" T0
                    WHERE T0.""U_Project"" = ? AND T0.""U_ActivityId"" = ?
                    GROUP BY 
                        T0.""U_Project"", T0.""U_ActivityId""";

                using var command = new OdbcCommand(query, connection);
                command.Parameters.AddWithValue("@ProjectId", projectId);
                command.Parameters.AddWithValue("@ActivityId", activityId);

                using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return new ActivityTimeTotal
                    {
                        Project = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                        ActivityId = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                        TimeTot = reader.IsDBNull(2) ? 0 : reader.GetDecimal(2)
                    };
                }

                return new ActivityTimeTotal
                {
                    Project = projectId,
                    ActivityId = activityId,
                    TimeTot = 0
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving activity time total for project {ProjectId} and activity {ActivityId}", projectId, activityId);
                throw;
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        // ERP financial mirror (read)
        //
        // NOTA: i nomi colonna seguono lo schema standard SAP B1 (OINV/INV1/INV6/JDT1/OJDT).
        // Su installazioni con localizzazioni particolari verificare i nomi rispetto alla company.
        // ──────────────────────────────────────────────────────────────────────

        public async Task<IEnumerable<ErpInvoiceDto>> GetInvoicesAsync(DateTime? since)
        {
            var result = new List<ErpInvoiceDto>();
            try
            {
                using var connection = await CreateOpenConnectionAsync();

                // Fatture A/R definitive (OINV / INV1 / INV6, ObjType 13).
                result.AddRange(await ReadInvoiceDocumentsAsync(
                    connection, "OINV", "INV1", "INV6", invType: 13, isCreditNote: false, since));

                // Note di credito A/R (ORIN / RIN1 / RIN6, ObjType 14): stesso shape delle fatture ma con
                // importi NEGATI e flag IsCreditNote, così il portale le somma come riduzione di fatturato ed
                // esposizione. Il numero documento è prefissato "NC-" per non collidere, lato idempotenza
                // portale (TenantId+Provider+ErpDocNumber), con eventuali fatture aventi lo stesso DocNum.
                result.AddRange(await ReadInvoiceDocumentsAsync(
                    connection, "ORIN", "RIN1", "RIN6", invType: 14, isCreditNote: true, since));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving invoices from database (since={Since})", since);
                throw;
            }

            return result;
        }

        /// <summary>
        /// Legge un blocco di documenti A/R (testate + righe + scadenzario + date incasso) da una coppia di
        /// tabelle SAP B1 omologhe: OINV/INV1/INV6 (fatture) oppure ORIN/RIN1/RIN6 (note di credito). Per le
        /// note di credito (<paramref name="isCreditNote"/>) tutti gli importi sono restituiti con segno negativo.
        /// </summary>
        private async Task<List<ErpInvoiceDto>> ReadInvoiceDocumentsAsync(
            OdbcConnection connection, string headerTable, string lineTable, string instTable,
            int invType, bool isCreditNote, DateTime? since)
        {
            var invoices = new Dictionary<int, ErpInvoiceDto>();
            var sign = isCreditNote ? -1m : 1m;

            // Gli UDF di correlazione AX.360 (U_AX360_InvId/DocType) sono provvisti SOLO su OINV/ODRF
            // (vedi EnsureAx360UserFieldsAsync), non su ORIN: le note di credito non nascono mai da una
            // bozza spinta da AX, quindi per le NC NON selezioniamo quelle colonne (non esistono su ORIN
            // e farebbero fallire l'intera query, rompendo anche il sync delle fatture).
            var udfInvId = Ax360Udf.Col(Ax360Udf.InvId);
            var udfDocType = Ax360Udf.Col(Ax360Udf.DocType);
            var udfSelect = isCreditNote ? string.Empty : $@", T.""{udfInvId}"", T.""{udfDocType}""";
            // Watermark incrementale: filtra per data di ULTIMA MODIFICA (UpdateDate), non per data di
            // emissione (DocDate). Quando in SAP B1 si applica un incasso, il documento viene aggiornato
            // (cambia PaidToDate) e UpdateDate avanza: filtrando su UpdateDate il sync incrementale
            // rivede i documenti saldati e propaga lo stato di pagamento. Filtrando su DocDate, invece,
            // un incasso su un documento già emesso non rientrerebbe MAI nel fetch incrementale e lo stato
            // resterebbe congelato a "aperto/scaduto" per sempre.
            var sinceClause = since.HasValue ? @"WHERE T.""UpdateDate"" >= ?" : string.Empty;

            // 1) Testate
            var headerQuery = $@"
                SELECT
                    T.""DocEntry"", T.""DocNum"", T.""CardCode"",
                    T.""DocDate"", T.""DocDueDate"", T.""DocCur"",
                    T.""DocTotal"", T.""VatSum"", T.""PaidToDate"", T.""Comments""{udfSelect}
                FROM ""{_schema}"".""{headerTable}"" T
                {sinceClause}
                ORDER BY T.""DocDate"", T.""DocEntry""";

            using (var command = new OdbcCommand(headerQuery, connection))
            {
                if (since.HasValue) command.Parameters.AddWithValue("@Since", since.Value);
                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var docEntry = reader.GetInt32(0);
                    var docTotal = reader.IsDBNull(6) ? 0m : reader.GetDecimal(6);
                    var vatSum = reader.IsDBNull(7) ? 0m : reader.GetDecimal(7);
                    // Colonne UDF presenti solo per le fatture (indici 10/11); assenti per le note di credito.
                    var docType = isCreditNote || reader.IsDBNull(11) ? null : reader.GetString(11);
                    var ax360InvId = isCreditNote || reader.IsDBNull(10) ? null : NullIfEmpty(reader.GetString(10));
                    var docNum = reader.IsDBNull(1) ? docEntry.ToString() : reader.GetInt32(1).ToString();
                    var dto = new ErpInvoiceDto
                    {
                        ErpDocId = docEntry.ToString(),
                        ErpDocNumber = isCreditNote ? $"NC-{docNum}" : docNum,
                        IsCreditNote = isCreditNote,
                        ErpCustomerCode = reader.IsDBNull(2) ? null : reader.GetString(2),
                        IssueDate = reader.IsDBNull(3) ? null : reader.GetDateTime(3),
                        DueDate = reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                        Currency = reader.IsDBNull(5) ? "EUR" : reader.GetString(5),
                        TotalAmount = sign * docTotal,
                        VatAmount = sign * vatSum,
                        TaxableAmount = sign * (docTotal - vatSum),
                        PaidAmount = sign * (reader.IsDBNull(8) ? 0m : reader.GetDecimal(8)),
                        Notes = reader.IsDBNull(9) ? null : reader.GetString(9),
                        Ax360InvoiceId = ax360InvId,
                        DocType = string.IsNullOrWhiteSpace(docType) ? "altro" : docType!,
                    };
                    invoices[docEntry] = dto;
                }
            }

            if (invoices.Count == 0) return new List<ErpInvoiceDto>();

            // 2) Righe (+ categoria di ricavo per la "Composizione dello scaduto" del portale): UDF
            //    sull'anagrafica articolo (OITM) per le righe a articolo, sul conto contabile (OACT)
            //    per le righe di servizio (ItemCode vuoto → si usa il conto della riga).
            var udfRevCat = Ax360Udf.Col(Ax360Udf.RevCat); // "U_AX360_RevCat"
            var lineQuery = $@"
                SELECT
                    L.""DocEntry"", L.""LineNum"", L.""ItemCode"", L.""Dscription"",
                    L.""Quantity"", L.""Price"", L.""LineTotal"", L.""VatPrcnt"", L.""GTotal"",
                    CASE WHEN L.""ItemCode"" IS NOT NULL AND L.""ItemCode"" <> ''
                         THEN ITM.""{udfRevCat}""
                         ELSE ACT.""{udfRevCat}"" END AS ""RevenueCategory""
                FROM ""{_schema}"".""{lineTable}"" L
                INNER JOIN ""{_schema}"".""{headerTable}"" H ON H.""DocEntry"" = L.""DocEntry""
                LEFT JOIN ""{_schema}"".""OITM"" ITM ON ITM.""ItemCode"" = L.""ItemCode""
                LEFT JOIN ""{_schema}"".""OACT"" ACT ON ACT.""AcctCode"" = L.""AcctCode""
                {(since.HasValue ? @"WHERE H.""UpdateDate"" >= ?" : string.Empty)}
                ORDER BY L.""DocEntry"", L.""LineNum""";

            using (var command = new OdbcCommand(lineQuery, connection))
            {
                if (since.HasValue) command.Parameters.AddWithValue("@Since", since.Value);
                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var docEntry = reader.GetInt32(0);
                    if (!invoices.TryGetValue(docEntry, out var inv)) continue;
                    var lineTotalNet = reader.IsDBNull(6) ? 0m : reader.GetDecimal(6);
                    var gross = reader.IsDBNull(8) ? lineTotalNet : reader.GetDecimal(8);
                    inv.Lines.Add(new ErpInvoiceLineDto
                    {
                        SortOrder = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                        ErpItemCode = reader.IsDBNull(2) ? null : reader.GetString(2),
                        Description = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                        Quantity = reader.IsDBNull(4) ? 0m : reader.GetDecimal(4),
                        UnitPrice = sign * (reader.IsDBNull(5) ? 0m : reader.GetDecimal(5)),
                        TaxableAmount = sign * lineTotalNet,
                        VatRate = reader.IsDBNull(7) ? 0m : reader.GetDecimal(7),
                        VatAmount = sign * (gross - lineTotalNet),
                        LineTotal = sign * gross,
                        RevenueCategory = reader.IsDBNull(9) || string.IsNullOrWhiteSpace(reader.GetString(9))
                            ? null
                            : reader.GetString(9).Trim(),
                    });
                }
            }

            // 2b) Date di incasso per rata (ORCT/RCT2), best-effort: arricchisce PaidAt sullo
            // scadenzario. Non incide sullo stato (calcolato dal portale su importi/scadenza):
            // è isolato in un try/catch così che, se lo schema di riconciliazione differisse su
            // questa company, PaidAt resti null senza rompere il mirror finanziario.
            var paidAtByInst = new Dictionary<(int DocEntry, int InstId), DateTime>();
            try
            {
                var paidAtQuery = $@"
                    SELECT R.""InvoiceId"", R.""InstId"", MAX(P.""DocDate"") AS ""PaidAt""
                    FROM ""{_schema}"".""RCT2"" R
                    INNER JOIN ""{_schema}"".""ORCT"" P ON P.""DocEntry"" = R.""DocNum""
                    INNER JOIN ""{_schema}"".""{headerTable}"" H ON H.""DocEntry"" = R.""InvoiceId""
                    WHERE R.""InvType"" = {invType} AND P.""Canceled"" = 'N'
                    {(since.HasValue ? @"AND H.""UpdateDate"" >= ?" : string.Empty)}
                    GROUP BY R.""InvoiceId"", R.""InstId""";

                using var paidAtCommand = new OdbcCommand(paidAtQuery, connection);
                if (since.HasValue) paidAtCommand.Parameters.AddWithValue("@Since", since.Value);
                using var paidAtReader = await paidAtCommand.ExecuteReaderAsync();
                while (await paidAtReader.ReadAsync())
                {
                    if (paidAtReader.IsDBNull(0) || paidAtReader.IsDBNull(1) || paidAtReader.IsDBNull(2)) continue;
                    // InstId, come INV6.InstlmntID, è SMALLINT su HANA: Convert.ToInt32 evita InvalidCast.
                    paidAtByInst[(paidAtReader.GetInt32(0), Convert.ToInt32(paidAtReader.GetValue(1)))] = paidAtReader.GetDateTime(2);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Date di incasso per rata non disponibili (RCT2/ORCT) per {Header}: PaidAt resterà null.", headerTable);
            }

            // 3) Scadenzario/rate
            var instQuery = $@"
                SELECT
                    I.""DocEntry"", I.""InstlmntID"", I.""DueDate"", I.""InsTotal"", I.""PaidToDate""
                FROM ""{_schema}"".""{instTable}"" I
                INNER JOIN ""{_schema}"".""{headerTable}"" H ON H.""DocEntry"" = I.""DocEntry""
                {(since.HasValue ? @"WHERE H.""UpdateDate"" >= ?" : string.Empty)}
                ORDER BY I.""DocEntry"", I.""InstlmntID""";

            using (var command = new OdbcCommand(instQuery, connection))
            {
                if (since.HasValue) command.Parameters.AddWithValue("@Since", since.Value);
                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var docEntry = reader.GetInt32(0);
                    if (!invoices.TryGetValue(docEntry, out var inv)) continue;
                    // INV6.InstlmntID è SMALLINT (Int16) nello schema HANA: GetInt32 lancia InvalidCast.
                    var instId = reader.IsDBNull(1) ? inv.Installments.Count + 1 : Convert.ToInt32(reader.GetValue(1));
                    inv.Installments.Add(new ErpPaymentInstallmentDto
                    {
                        InstallmentNumber = instId,
                        DueDate = reader.IsDBNull(2) ? (inv.DueDate ?? default) : reader.GetDateTime(2),
                        Amount = sign * (reader.IsDBNull(3) ? 0m : reader.GetDecimal(3)),
                        PaidAmount = sign * (reader.IsDBNull(4) ? 0m : reader.GetDecimal(4)),
                        PaidAt = paidAtByInst.TryGetValue((docEntry, instId), out var pa) ? pa : null,
                    });
                }
            }

            return invoices.Values.ToList();
        }

        public async Task<IEnumerable<ErpLedgerEntryDto>> GetLedgerAsync(string? customerCode, DateTime? since)
        {
            var entries = new List<ErpLedgerEntryDto>();
            try
            {
                using var connection = await CreateOpenConnectionAsync();

                var conditions = new List<string> { @"J.""ShortName"" IS NOT NULL" };
                if (!string.IsNullOrWhiteSpace(customerCode)) conditions.Add(@"J.""ShortName"" = ?");
                if (since.HasValue) conditions.Add(@"J.""RefDate"" >= ?");
                var whereClause = "WHERE " + string.Join(" AND ", conditions);

                // JDT1 = righe di registrazione contabile; OJDT = testata (TransType per classificazione).
                // JDT1."ShortName" vale il CardCode sulle righe di business partner ma il codice CONTO su
                // quelle di contabilita': senza il join su OCRD questo endpoint restituirebbe l'intero libro
                // giornale (157k righe su MTF contro 15k di partitario clienti), che il consumer scarta
                // comunque non trovando il cliente. Qui si serve il partitario CLIENTI, come da contratto.
                var query = $@"
                    SELECT
                        J.""ShortName"", J.""TransId"", J.""Ref1"", J.""RefDate"", J.""DueDate"",
                        J.""Debit"", J.""Credit"", J.""LineMemo"", H.""TransType""
                    FROM ""{_schema}"".""JDT1"" J
                    INNER JOIN ""{_schema}"".""OJDT"" H ON H.""TransId"" = J.""TransId""
                    INNER JOIN ""{_schema}"".""OCRD"" C ON C.""CardCode"" = J.""ShortName"" AND C.""CardType"" = 'C'
                    {whereClause}
                    ORDER BY J.""ShortName"", J.""RefDate"", J.""TransId""";

                using var command = new OdbcCommand(query, connection);
                if (!string.IsNullOrWhiteSpace(customerCode)) command.Parameters.AddWithValue("@CardCode", customerCode);
                if (since.HasValue) command.Parameters.AddWithValue("@Since", since.Value);

                var runningBalance = new Dictionary<string, decimal>();
                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var shortName = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                    var debit = reader.IsDBNull(5) ? 0m : reader.GetDecimal(5);
                    var credit = reader.IsDBNull(6) ? 0m : reader.GetDecimal(6);
                    runningBalance.TryGetValue(shortName, out var prev);
                    var balance = prev + debit - credit;
                    runningBalance[shortName] = balance;

                    entries.Add(new ErpLedgerEntryDto
                    {
                        ErpCustomerCode = shortName,
                        ErpDocNumber = reader.IsDBNull(2) ? (reader.IsDBNull(1) ? string.Empty : reader.GetInt32(1).ToString()) : reader.GetString(2),
                        EntryDate = reader.IsDBNull(3) ? default : reader.GetDateTime(3),
                        DueDate = reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                        DocType = MapLedgerDocType(ReadNullableInt(reader, 8)),
                        Description = reader.IsDBNull(7) ? null : reader.GetString(7),
                        Currency = "EUR",
                        Debit = debit,
                        Credit = credit,
                        Balance = balance,
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving ledger from database (customer={Customer}, since={Since})", customerCode, since);
                throw;
            }
            return entries;
        }

        public async Task<ExistingErpDocument?> FindDocumentByCorrelationIdAsync(string ax360InvoiceId)
        {
            if (string.IsNullOrWhiteSpace(ax360InvoiceId)) return null;
            var udfInvId = Ax360Udf.Col(Ax360Udf.InvId);
            try
            {
                using var connection = await CreateOpenConnectionAsync();

                // Bozze (ODRF) e fatture definitive (OINV) condividono l'UDF di correlazione.
                // Le bozze A/R sono filtrate per ObjType = '13' (oInvoices).
                var query = $@"
                    SELECT ""DocEntry"", ""DocNum"", 'posted' AS ""Kind""
                    FROM ""{_schema}"".""OINV"" WHERE ""{udfInvId}"" = ?
                    UNION ALL
                    SELECT ""DocEntry"", ""DocNum"", 'draft' AS ""Kind""
                    FROM ""{_schema}"".""ODRF"" WHERE ""{udfInvId}"" = ? AND ""ObjType"" = '13'";

                using var command = new OdbcCommand(query, connection);
                command.Parameters.AddWithValue("@InvId1", ax360InvoiceId);
                command.Parameters.AddWithValue("@InvId2", ax360InvoiceId);
                using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return new ExistingErpDocument
                    {
                        ErpDocId = reader.IsDBNull(0) ? string.Empty : reader.GetInt32(0).ToString(),
                        ErpDocNumber = reader.IsDBNull(1) ? string.Empty : reader.GetInt32(1).ToString(),
                        DocStatus = reader.IsDBNull(2) ? "draft" : reader.GetString(2),
                    };
                }
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking correlation id {InvId} in database", ax360InvoiceId);
                throw;
            }
        }

        private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

        /// <summary>
        /// Legge un intero da una colonna il cui tipo HANA non e' garantito numerico.
        /// Serve per OJDT."TransType", che su HANA e' NVARCHAR(20) con valori "13"/"24"/"-2":
        /// <c>GetInt32</c> ci lanciava InvalidCastException facendo fallire l'intero partitario.
        /// Tollera anche INTEGER/SMALLINT, cosi' regge differenze di schema tra company.
        /// </summary>
        private static int? ReadNullableInt(System.Data.Common.DbDataReader reader, int ordinal)
        {
            if (reader.IsDBNull(ordinal)) return null;
            var raw = reader.GetValue(ordinal);
            return raw switch
            {
                int i => i,
                short s => s,
                long l => (int)l,
                decimal d => (int)d,
                _ => int.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture)?.Trim(),
                        NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                     ? parsed
                     : null,
            };
        }

        private static string MapLedgerDocType(int? transType) => transType switch
        {
            13 => "fattura",        // A/R Invoice
            14 => "nota_credito",   // A/R Credit Memo
            24 => "pagamento",      // Incoming Payment
            _ => "altro",
        };
    }
}
