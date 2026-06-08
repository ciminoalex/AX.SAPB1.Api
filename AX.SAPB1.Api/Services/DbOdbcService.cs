using System.Data.Odbc;
using AX.SAPB1.Api.Models;
using Microsoft.AspNetCore.Http;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace AX.SAPB1.Api.Services
{
    public class DbOdbcService : IDbOdbcService
    {
        private readonly string _connectionString;
        private readonly string _schema;
        private readonly ILogger<DbOdbcService> _logger;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public DbOdbcService(IConfiguration configuration, ILogger<DbOdbcService> logger, IHttpContextAccessor httpContextAccessor)
        {
            _connectionString = configuration.GetConnectionString("DefaultDatabase") 
                ?? throw new ArgumentNullException(nameof(configuration), "DefaultDatabase connection string not found");

            _schema = configuration["SapB1:CompanyDB"]
                ?? throw new ArgumentNullException(nameof(configuration), "Schema not defined");


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
                var query = $@"
                    SELECT
                        T.""AbsEntry"" AS ""Code"",
                        T.""NAME"" AS ""Name"",
                        T.""CARDCODE"" AS ""CardCode"",
                        C.""CardName"" AS ""CardName""
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
                        CardName = reader.IsDBNull(3) ? null : reader.GetString(3)
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
                var query = $@"
                    SELECT
                        T.""AbsEntry"" AS ""Code"",
                        T.""NAME"" AS ""Name""
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
                        Name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1)
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
                var userName =
                    principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                    ?? principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? principal?.Claims.FirstOrDefault(c => c.Type == "preferred_username")?.Value
                    ?? principal?.Claims.FirstOrDefault(c => c.Type == "unique_name")?.Value
                    ?? principal?.FindFirst(ClaimTypes.Name)?.Value;
                _logger.LogDebug("GetResourcesAsync principal resolved: sub/name={User}, jti={Jti}", userName ?? "", jti ?? "");
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
                    // Auth disabilitata o token non disponibile: restituisce tutte le risorse attive.
                    _logger.LogInformation("GetResourcesAsync: missing JWT user claim, returning all active resources");
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
            var invoices = new Dictionary<int, ErpInvoiceDto>();
            var byDocEntry = new Dictionary<string, int>(); // ErpDocId(string) → DocEntry(int) per binding righe/rate

            var udfInvId = Ax360Udf.Col(Ax360Udf.InvId);
            var udfDocType = Ax360Udf.Col(Ax360Udf.DocType);
            var sinceClause = since.HasValue ? @"WHERE T.""DocDate"" >= ?" : string.Empty;

            try
            {
                using var connection = await CreateOpenConnectionAsync();

                // 1) Testate fattura
                var headerQuery = $@"
                    SELECT
                        T.""DocEntry"", T.""DocNum"", T.""CardCode"",
                        T.""DocDate"", T.""DocDueDate"", T.""DocCur"",
                        T.""DocTotal"", T.""VatSum"", T.""PaidToDate"", T.""Comments"",
                        T.""{udfInvId}"", T.""{udfDocType}""
                    FROM ""{_schema}"".""OINV"" T
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
                        var docType = reader.IsDBNull(11) ? null : reader.GetString(11);
                        var dto = new ErpInvoiceDto
                        {
                            ErpDocId = docEntry.ToString(),
                            ErpDocNumber = reader.IsDBNull(1) ? docEntry.ToString() : reader.GetInt32(1).ToString(),
                            ErpCustomerCode = reader.IsDBNull(2) ? null : reader.GetString(2),
                            IssueDate = reader.IsDBNull(3) ? null : reader.GetDateTime(3),
                            DueDate = reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                            Currency = reader.IsDBNull(5) ? "EUR" : reader.GetString(5),
                            TotalAmount = docTotal,
                            VatAmount = vatSum,
                            TaxableAmount = docTotal - vatSum,
                            PaidAmount = reader.IsDBNull(8) ? 0m : reader.GetDecimal(8),
                            Notes = reader.IsDBNull(9) ? null : reader.GetString(9),
                            Ax360InvoiceId = reader.IsDBNull(10) ? null : NullIfEmpty(reader.GetString(10)),
                            DocType = string.IsNullOrWhiteSpace(docType) ? "altro" : docType!,
                        };
                        invoices[docEntry] = dto;
                        byDocEntry[dto.ErpDocId!] = docEntry;
                    }
                }

                if (invoices.Count == 0) return invoices.Values.ToList();

                // 2) Righe fattura (INV1)
                // Categoria di ricavo risolta lato servizio: precedenza all'UDF di RIGA (INV1.U_AX360_RevCat),
                // fallback all'UDF dell'ARTICOLO (OITM.U_AX360_RevCat); così gli articoli "contenitore"
                // (generici) sono classificabili per singola riga. Il portale riceve solo la stringa risolta.
                var udfRevCat = Ax360Udf.Col(Ax360Udf.RevCat);
                var lineQuery = $@"
                    SELECT
                        L.""DocEntry"", L.""LineNum"", L.""ItemCode"", L.""Dscription"",
                        L.""Quantity"", L.""Price"", L.""LineTotal"", L.""VatPrcnt"", L.""GTotal"",
                        COALESCE(NULLIF(L.""{udfRevCat}"", ''), NULLIF(I.""{udfRevCat}"", '')) AS ""RevCat""
                    FROM ""{_schema}"".""INV1"" L
                    INNER JOIN ""{_schema}"".""OINV"" H ON H.""DocEntry"" = L.""DocEntry""
                    LEFT JOIN ""{_schema}"".""OITM"" I ON I.""ItemCode"" = L.""ItemCode""
                    {(since.HasValue ? @"WHERE H.""DocDate"" >= ?" : string.Empty)}
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
                            UnitPrice = reader.IsDBNull(5) ? 0m : reader.GetDecimal(5),
                            TaxableAmount = lineTotalNet,
                            VatRate = reader.IsDBNull(7) ? 0m : reader.GetDecimal(7),
                            VatAmount = gross - lineTotalNet,
                            LineTotal = gross,
                            Category = reader.IsDBNull(9) ? null : NullIfEmpty(reader.GetString(9)),
                        });
                    }
                }

                // 3) Scadenzario/rate (INV6)
                var instQuery = $@"
                    SELECT
                        I.""DocEntry"", I.""InstlmntID"", I.""DueDate"", I.""InsTotal"", I.""PaidToDate""
                    FROM ""{_schema}"".""INV6"" I
                    INNER JOIN ""{_schema}"".""OINV"" H ON H.""DocEntry"" = I.""DocEntry""
                    {(since.HasValue ? @"WHERE H.""DocDate"" >= ?" : string.Empty)}
                    ORDER BY I.""DocEntry"", I.""InstlmntID""";

                using (var command = new OdbcCommand(instQuery, connection))
                {
                    if (since.HasValue) command.Parameters.AddWithValue("@Since", since.Value);
                    using var reader = await command.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var docEntry = reader.GetInt32(0);
                        if (!invoices.TryGetValue(docEntry, out var inv)) continue;
                        inv.Installments.Add(new ErpPaymentInstallmentDto
                        {
                            // INV6.InstlmntID è SMALLINT (Int16) nello schema HANA: GetInt32 lancia InvalidCast.
                            InstallmentNumber = reader.IsDBNull(1) ? inv.Installments.Count + 1 : Convert.ToInt32(reader.GetValue(1)),
                            DueDate = reader.IsDBNull(2) ? (inv.DueDate ?? default) : reader.GetDateTime(2),
                            Amount = reader.IsDBNull(3) ? 0m : reader.GetDecimal(3),
                            PaidAmount = reader.IsDBNull(4) ? 0m : reader.GetDecimal(4),
                            PaidAt = null,
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving invoices from database (since={Since})", since);
                throw;
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
                var query = $@"
                    SELECT
                        J.""ShortName"", J.""TransId"", J.""Ref1"", J.""RefDate"", J.""DueDate"",
                        J.""Debit"", J.""Credit"", J.""LineMemo"", H.""TransType""
                    FROM ""{_schema}"".""JDT1"" J
                    INNER JOIN ""{_schema}"".""OJDT"" H ON H.""TransId"" = J.""TransId""
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
                        DocType = MapLedgerDocType(reader.IsDBNull(8) ? (int?)null : reader.GetInt32(8)),
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

        private static string MapLedgerDocType(int? transType) => transType switch
        {
            13 => "fattura",        // A/R Invoice
            14 => "nota_credito",   // A/R Credit Memo
            24 => "pagamento",      // Incoming Payment
            _ => "altro",
        };
    }
}
