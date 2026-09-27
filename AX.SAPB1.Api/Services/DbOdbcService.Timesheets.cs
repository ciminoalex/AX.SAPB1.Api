using System.Data.Odbc;
using AX.SAPB1.Api.Models;

namespace AX.SAPB1.Api.Services
{
    /// <summary>
    /// Letture di <c>@SGS_PRJ_OTMS</c> a supporto dell'aggiornamento ore dal portale
    /// (<c>PATCH /api/timesheet/{docEntry}/hours</c>). Sola lettura: la scrittura passa dal Service Layer.
    /// </summary>
    public partial class DbOdbcService
    {
        /// <summary>
        /// Stato corrente di una riga di timesheet per <c>DocEntry</c> (MAI <c>Code</c>: è l'identificativo che il
        /// portale conserva; le due colonne divergono sull'11,8% delle righe). Null se la riga non esiste.
        /// <para>
        /// Tutte le colonne si leggono con <c>GetValue</c> e i parser tolleranti (<see cref="ParseErpText"/>,
        /// <see cref="ParseNullableHours"/>), mai con <c>GetDecimal</c>/<c>GetInt32</c>: su questo impianto
        /// <c>U_TimeNrTot</c>/<c>U_TimeNrNet</c> possono arrivare come testo e <c>U_DestEntry</c> può essere
        /// alfanumerico. <c>U_DestType</c> non serve: per decidere basta sapere se un documento di
        /// destinazione c'è, non di che tipo sia.
        /// </para>
        /// </summary>
        public async Task<TimesheetHoursState?> GetTimesheetHoursStateAsync(int docEntry)
        {
            try
            {
                using var connection = await CreateOpenConnectionAsync();

                var query = $@"
                    SELECT ""Code"", ""Canceled"", ""U_Status"", ""U_DestEntry"", ""U_TimeNrTot"", ""U_TimeNrNet""
                    FROM ""{_schema}"".""@SGS_PRJ_OTMS""
                    WHERE ""DocEntry"" = ?";

                using var command = new OdbcCommand(query, connection);
                command.Parameters.AddWithValue("@DocEntry", docEntry);

                using var reader = await command.ExecuteReaderAsync();
                if (!await reader.ReadAsync()) return null;

                return MapTimesheetHoursState(
                    docEntry,
                    code: reader.IsDBNull(0) ? null : reader.GetValue(0),
                    canceled: reader.IsDBNull(1) ? null : reader.GetValue(1),
                    status: reader.IsDBNull(2) ? null : reader.GetValue(2),
                    destEntry: reader.IsDBNull(3) ? null : reader.GetValue(3),
                    totalHours: reader.IsDBNull(4) ? null : reader.GetValue(4),
                    billableHours: reader.IsDBNull(5) ? null : reader.GetValue(5));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving hours state of timesheet with DocEntry {DocEntry} from database", docEntry);
                throw;
            }
        }

        /// <summary>
        /// Traduzione dei valori grezzi del driver ODBC nello stato della riga. Separata dalla query per poterla
        /// verificare senza database: i valori testuali (ore "2.5", documento "0") sono il caso che conta.
        /// </summary>
        internal static TimesheetHoursState MapTimesheetHoursState(
            int docEntry, object? code, object? canceled, object? status, object? destEntry, object? totalHours, object? billableHours)
            => new()
            {
                DocEntry = docEntry,
                Code = ParseErpText(code),
                Canceled = ParseErpText(canceled),
                Status = ParseErpText(status),
                DestEntry = ParseErpText(destEntry),
                TotalHours = ParseNullableHours(totalHours),
                BillableHours = ParseNullableHours(billableHours),
            };

        /// <summary>
        /// Legge la colonna di sistema <c>Canceled</c> di <c>@SGS_PRJ_OTMS</c> per <see cref="Timesheet.Canceled"/>:
        /// <c>'Y'</c> = <c>true</c>, <c>'N'</c> = <c>false</c> (spazi ai bordi e maiuscole tollerati), qualunque altra
        /// cosa — assente, vuota, un valore imprevisto — <c>null</c>. Il valore grezzo passa da
        /// <see cref="ParseErpText"/>: può arrivare dal driver come stringa, carattere o altro tipo, e si converte a
        /// cultura invariante. Un valore illeggibile NON diventa <c>false</c>: "attiva" detto senza saperlo farebbe
        /// scambiare al portale una riga annullata per la riga creata dal suo push.
        /// </summary>
        internal static bool? ParseCanceledFlag(object? raw)
        {
            var text = ParseErpText(raw);
            if (string.Equals(text, "Y", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(text, "N", StringComparison.OrdinalIgnoreCase)) return false;
            return null;
        }
    }
}
