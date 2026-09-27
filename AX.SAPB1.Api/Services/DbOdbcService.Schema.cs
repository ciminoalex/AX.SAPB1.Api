using System.Collections.Concurrent;
using System.Data.Odbc;

namespace AX.SAPB1.Api.Services
{
    /// <summary>
    /// Metadati dello schema della company: colonne e lunghezze delle tabelle, campi utente presenti.
    ///
    /// <para><b>Perché serve.</b> Una colonna assente in una SELECT fa fallire l'INTERA query (è già successo
    /// con gli UDF AX360 su ORIN), e un campo utente sconosciuto in un POST fa rifiutare l'intero documento
    /// dal Service Layer. Le lunghezze servono a troncare lato servizio: nessun vincolo le fa rispettare prima
    /// che il Service Layer rifiuti il documento.</para>
    ///
    /// <para><b>Due fonti per i campi utente, in unione.</b> <c>CUFD</c> è l'anagrafica degli UDF, ma per i
    /// documenti di marketing registra la testata sotto <c>TableID = 'OINV'</c> anche per le colonne che SAP
    /// crea fisicamente su ORDR/ODRF (vedi DBSetup di MTF.FatturazioneElettronica: gli UDF FE sono creati su
    /// "OINV"). Chiedere a CUFD solo <c>TableID = 'ORDR'</c> direbbe "assente" per un campo che c'è. La verità
    /// fisica sta in <c>SYS.TABLE_COLUMNS</c>; CUFD resta come seconda fonte se la vista di sistema non è
    /// leggibile. Se falliscono entrambe si lancia: meglio un errore chiaro che un documento senza correlazione.</para>
    ///
    /// <para>Cache statica (il servizio è Scoped) per schema+tabella, 10 minuti: i metadati cambiano solo
    /// con un provisioning, e chi ha bisogno di un campo obbligatorio può forzare la rilettura.</para>
    /// </summary>
    public partial class DbOdbcService
    {
        private static readonly TimeSpan SchemaCacheTtl = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan SchemaCacheFailureTtl = TimeSpan.FromMinutes(1);

        private static readonly ConcurrentDictionary<string, (DateTime ExpiresUtc, IReadOnlyDictionary<string, int> Columns)> _tableColumnsCache = new();
        private static readonly ConcurrentDictionary<string, (DateTime ExpiresUtc, IReadOnlySet<string> Fields)> _userFieldsCache = new();

        /// <summary>
        /// Colonne della tabella con la loro lunghezza massima (caratteri per i tipi testo), da
        /// <c>SYS.TABLE_COLUMNS</c>. Dizionario vuoto se la vista non è leggibile: il chiamante usa le
        /// lunghezze di ripiego documentate.
        /// </summary>
        public async Task<IReadOnlyDictionary<string, int>> GetColumnLengthsAsync(string table, bool forceRefresh = false)
        {
            var key = $"{_schema}|{table}";
            if (!forceRefresh && _tableColumnsCache.TryGetValue(key, out var cached) && cached.ExpiresUtc > DateTime.UtcNow)
                return cached.Columns;

            var (columns, ok) = await ReadTableColumnsAsync(table);
            _tableColumnsCache[key] = (DateTime.UtcNow + (ok ? SchemaCacheTtl : SchemaCacheFailureTtl), columns);
            return columns;
        }

        /// <summary>
        /// Nomi fisici (<c>U_…</c>) dei campi utente presenti sulla tabella: unione di <c>SYS.TABLE_COLUMNS</c>
        /// e <c>CUFD</c> (vedi la nota sulla classe). Lancia se nessuna delle due fonti è leggibile.
        /// </summary>
        public async Task<IReadOnlySet<string>> GetUserFieldColumnsAsync(string table, bool forceRefresh = false)
        {
            var key = $"{_schema}|{table}";
            if (!forceRefresh && _userFieldsCache.TryGetValue(key, out var cached) && cached.ExpiresUtc > DateTime.UtcNow)
                return cached.Fields;

            var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var (columns, physicalOk) = await ReadTableColumnsAsync(table);
            _tableColumnsCache[key] = (DateTime.UtcNow + (physicalOk ? SchemaCacheTtl : SchemaCacheFailureTtl), columns);
            foreach (var c in columns.Keys.Where(c => c.StartsWith("U_", StringComparison.Ordinal)))
                fields.Add(c);

            var cufdOk = true;
            try
            {
                using var connection = await CreateOpenConnectionAsync();
                using var command = new OdbcCommand($@"SELECT ""AliasID"" FROM ""{_schema}"".""CUFD"" WHERE ""TableID"" = ?", connection);
                command.Parameters.AddWithValue("@TableID", table);
                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var alias = ParseErpText(reader.IsDBNull(0) ? null : reader.GetValue(0));
                    if (alias != null) fields.Add("U_" + alias);
                }
            }
            catch (Exception ex)
            {
                cufdOk = false;
                _logger.LogWarning(ex, "Lettura di CUFD per la tabella {Table} non riuscita.", table);
            }

            if (!physicalOk && !cufdOk)
                throw new InvalidOperationException($"Impossibile verificare i campi utente della tabella {table}: né SYS.TABLE_COLUMNS né CUFD sono leggibili.");

            _userFieldsCache[key] = (DateTime.UtcNow + SchemaCacheTtl, fields);
            return fields;
        }

        private async Task<(IReadOnlyDictionary<string, int> Columns, bool Ok)> ReadTableColumnsAsync(string table)
        {
            var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var connection = await CreateOpenConnectionAsync();
                using var command = new OdbcCommand(
                    @"SELECT ""COLUMN_NAME"", ""LENGTH"" FROM ""SYS"".""TABLE_COLUMNS"" WHERE ""SCHEMA_NAME"" = ? AND ""TABLE_NAME"" = ?",
                    connection);
                command.Parameters.AddWithValue("@Schema", _schema);
                command.Parameters.AddWithValue("@Table", table);
                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var name = ParseErpText(reader.IsDBNull(0) ? null : reader.GetValue(0));
                    if (name == null) continue;
                    columns[name] = ReadNullableInt(reader, 1) ?? 0;
                }
                return (columns, true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Lettura di SYS.TABLE_COLUMNS per {Schema}.{Table} non riuscita: si useranno le lunghezze di ripiego.", _schema, table);
                return (columns, false);
            }
        }
    }
}
