using System.Data.Odbc;
using System.Globalization;
using AX.SAPB1.Api.Models;
using AX.SAPB1.Api.Services.SalesDocuments;

namespace AX.SAPB1.Api.Services
{
    /// <summary>
    /// Letture ODBC a supporto dei documenti di vendita spinti dal portale (fattura/ordine, bozza/definitivo),
    /// degli ordini correlati, degli articoli vendibili e dei progetti contabili. Sola lettura: le scritture
    /// passano dal Service Layer. Ogni tabella è qualificata con lo schema della configurazione
    /// (<c>SapB1:CompanyDB</c>), così la stessa build serve anche un'istanza puntata su una company di test.
    /// </summary>
    public partial class DbOdbcService
    {
        /// <summary>Tabelle in cui cercare un documento di vendita per correlationId.</summary>
        internal static readonly string[] CorrelationLookupTables = { "OINV", "ORDR", "ODRF" };

        /// <summary>
        /// Documenti (fatture, ordini o bozze di uno dei due) già marcati con il correlationId del portale in
        /// <c>U_AX360_InvId</c>. <paramref name="tables"/> elenca le sole tabelle su cui il campo ESISTE (vedi
        /// <see cref="GetUserFieldColumnsAsync"/>): una tabella senza la colonna farebbe fallire l'intera UNION.
        /// Possono essere più d'uno (bozza confermata, fattura tratta da un ordine che ne eredita l'UDF,
        /// documento annullato e suo annullamento): li si restituisce tutti, con un ORDER BY deterministico, e
        /// sceglie il chiamante con <see cref="SalesDocumentPayloadBuilder.SelectExisting"/>.
        /// <para>Una bozza già trasformata nel definitivo (<c>ODRF.DocStatus = 'C'</c>) resta in ODRF con
        /// <c>CANCELED = 'N'</c>: la si restituisce marcata (<see cref="ExistingSalesDocument.ConvertedDraft"/>) e in
        /// fondo, dopo anche i documenti annullati. Prima vinceva lei su un definitivo annullato in SAP, e il
        /// portale vedeva «bozza valida» un documento che non esiste più.</para>
        /// </summary>
        public async Task<IReadOnlyList<ExistingSalesDocument>> FindSalesDocumentsByCorrelationIdAsync(string correlationId, IReadOnlyCollection<string> tables)
        {
            if (string.IsNullOrWhiteSpace(correlationId) || tables.Count == 0) return Array.Empty<ExistingSalesDocument>();
            var udf = Ax360Udf.Col(Ax360Udf.InvId);

            const string columns = @"""DocEntry"", ""DocNum"", ""DocTotal"", ""VatSum"", ""DocDueDate"", ""CANCELED""";
            const string notConverted = @"CAST('N' AS NVARCHAR(1)) AS ""Converted""";
            var parts = new List<string>();
            if (tables.Contains("OINV", StringComparer.OrdinalIgnoreCase))
                parts.Add($@"SELECT CAST('OINV' AS NVARCHAR(4)) AS ""Src"", {columns}, CAST('invoice' AS NVARCHAR(10)) AS ""Kind"", CAST('posted' AS NVARCHAR(10)) AS ""Status"", {notConverted}
                    FROM ""{_schema}"".""OINV"" WHERE ""{udf}"" = ?");
            if (tables.Contains("ORDR", StringComparer.OrdinalIgnoreCase))
                parts.Add($@"SELECT CAST('ORDR' AS NVARCHAR(4)) AS ""Src"", {columns}, CAST('order' AS NVARCHAR(10)) AS ""Kind"", CAST('posted' AS NVARCHAR(10)) AS ""Status"", {notConverted}
                    FROM ""{_schema}"".""ORDR"" WHERE ""{udf}"" = ?");
            if (tables.Contains("ODRF", StringComparer.OrdinalIgnoreCase))
                parts.Add($@"SELECT CAST('ODRF' AS NVARCHAR(4)) AS ""Src"", {columns},
                        CAST(CASE ""ObjType"" WHEN '17' THEN 'order' ELSE 'invoice' END AS NVARCHAR(10)) AS ""Kind"", CAST('draft' AS NVARCHAR(10)) AS ""Status"",
                        CAST(CASE ""DocStatus"" WHEN 'C' THEN 'Y' ELSE 'N' END AS NVARCHAR(1)) AS ""Converted""
                    FROM ""{_schema}"".""ODRF"" WHERE ""{udf}"" = ? AND ""ObjType"" IN ('13', '17')");
            if (parts.Count == 0) return Array.Empty<ExistingSalesDocument>();

            // Colonna nuova in coda (ordinale 9): gli ordinali letti sotto non si spostano.
            var query = $@"
                SELECT X.""Src"", X.""DocEntry"", X.""DocNum"", X.""DocTotal"", X.""VatSum"", X.""DocDueDate"", X.""CANCELED"", X.""Kind"", X.""Status"", X.""Converted""
                FROM ({string.Join("\n UNION ALL \n", parts)}) X
                ORDER BY CASE X.""Converted"" WHEN 'Y' THEN 1 ELSE 0 END,
                         CASE X.""CANCELED"" WHEN 'N' THEN 0 WHEN 'Y' THEN 1 ELSE 2 END,
                         CASE X.""Status"" WHEN 'posted' THEN 0 ELSE 1 END,
                         X.""DocEntry"", X.""Kind""";

            try
            {
                using var connection = await CreateOpenConnectionAsync();
                using var command = new OdbcCommand(query, connection);
                // Un parametro per ramo della UNION, nello stesso ordine dei '?'.
                foreach (var _ in parts) command.Parameters.AddWithValue("@InvId", correlationId.Trim());

                var found = new List<ExistingSalesDocument>();
                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    found.Add(new ExistingSalesDocument
                    {
                        Table = ParseErpText(reader.GetValue(0)) ?? string.Empty,
                        DocEntry = Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture),
                        DocNum = ReadNullableInt(reader, 2),
                        DocTotal = ReadNullableDecimal(reader, 3),
                        VatSum = ReadNullableDecimal(reader, 4),
                        DocDueDate = reader.IsDBNull(5) ? null : reader.GetDateTime(5),
                        Canceled = ParseErpText(reader.IsDBNull(6) ? null : reader.GetValue(6)) ?? "N",
                        DocumentKind = ParseErpText(reader.GetValue(7)) ?? SalesDocumentKinds.Invoice,
                        Status = ParseErpText(reader.GetValue(8)) ?? SalesDocumentKinds.Draft,
                        ConvertedDraft = string.Equals(ParseErpText(reader.IsDBNull(9) ? null : reader.GetValue(9)), "Y", StringComparison.OrdinalIgnoreCase),
                    });
                }
                return found;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Errore nella ricerca del documento di vendita con correlationId {CorrelationId}", correlationId);
                throw;
            }
        }

        /// <summary>
        /// Unità di misura per le righe del documento: <c>OUOM.UomCode → UomEntry</c> e, per ogni articolo, il
        /// gruppo UdM (<c>OITM.UgpEntry</c>, -1 = manuale) con le unità che contiene (<c>UGP1</c>).
        /// </summary>
        public async Task<UomResolution> ResolveUnitsOfMeasureAsync(IReadOnlyCollection<string> itemCodes, IReadOnlyCollection<string> uomCodes)
        {
            var result = new UomResolution();
            if (uomCodes.Count == 0) return result;

            using var connection = await CreateOpenConnectionAsync();

            using (var command = new OdbcCommand(
                $@"SELECT ""UomEntry"", ""UomCode"" FROM ""{_schema}"".""OUOM"" WHERE ""UomCode"" IN ({Placeholders(uomCodes.Count)})",
                connection))
            {
                foreach (var code in uomCodes) command.Parameters.AddWithValue("@UomCode", code);
                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var entry = ReadNullableInt(reader, 0);
                    var code = ParseErpText(reader.IsDBNull(1) ? null : reader.GetValue(1));
                    if (entry != null && code != null) result.UomEntryByCode[code] = entry.Value;
                }
            }

            if (itemCodes.Count == 0) return result;

            var groups = new Dictionary<string, (int Ugp, HashSet<int> Entries)>(StringComparer.OrdinalIgnoreCase);
            using (var command = new OdbcCommand(
                $@"SELECT I.""ItemCode"", I.""UgpEntry"", G.""UomEntry""
                   FROM ""{_schema}"".""OITM"" I
                   LEFT JOIN ""{_schema}"".""UGP1"" G ON G.""UgpEntry"" = I.""UgpEntry""
                   WHERE I.""ItemCode"" IN ({Placeholders(itemCodes.Count)})",
                connection))
            {
                foreach (var code in itemCodes) command.Parameters.AddWithValue("@ItemCode", code);
                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var item = ParseErpText(reader.IsDBNull(0) ? null : reader.GetValue(0));
                    if (item == null) continue;
                    if (!groups.TryGetValue(item, out var g))
                    {
                        g = (ReadNullableInt(reader, 1) ?? -1, new HashSet<int>());
                        groups[item] = g;
                    }
                    var uomEntry = ReadNullableInt(reader, 2);
                    if (uomEntry != null) g.Entries.Add(uomEntry.Value);
                }
            }

            foreach (var (item, g) in groups)
                result.Items[item] = new ItemUomInfo(g.Ugp, g.Entries);
            return result;
        }

        /// <summary>
        /// Ordini cliente nati dal portale (<c>U_AX360_InvId</c> valorizzato), con stato, residuo e le fatture
        /// tratte dall'ordine. Con <paramref name="since"/> il filtro è per data di ULTIMA MODIFICA: una fattura
        /// tratta dall'ordine lo chiude e ne avanza <c>UpdateDate</c>, quindi rientra nel delta. Se il campo di
        /// correlazione non esiste su ORDR non c'è nulla da correlare: lista vuota, senza far fallire la query.
        /// </summary>
        public async Task<IEnumerable<ErpSalesOrderDto>> GetSalesOrdersAsync(DateTime? since)
        {
            var udf = Ax360Udf.Col(Ax360Udf.InvId);
            var fields = await GetUserFieldColumnsAsync("ORDR");
            if (!fields.Contains(udf))
            {
                _logger.LogWarning("GetSalesOrders: il campo {Udf} non esiste su ORDR ({Schema}): nessun ordine correlabile.", udf, _schema);
                return Array.Empty<ErpSalesOrderDto>();
            }

            var orders = new Dictionary<int, ErpSalesOrderDto>();
            var sinceClause = since.HasValue ? @" AND O.""UpdateDate"" >= ?" : string.Empty;

            try
            {
                using var connection = await CreateOpenConnectionAsync();

                var headerQuery = $@"
                    SELECT O.""DocEntry"", O.""DocNum"", O.""CardCode"", O.""DocDate"", O.""DocStatus"", O.""CANCELED"",
                           O.""DocTotal"", O.""{udf}"",
                           (SELECT IFNULL(SUM(R.""OpenSum""), 0) FROM ""{_schema}"".""RDR1"" R WHERE R.""DocEntry"" = O.""DocEntry"") AS ""OpenSum""
                    FROM ""{_schema}"".""ORDR"" O
                    WHERE IFNULL(O.""{udf}"", '') <> ''{sinceClause}
                    ORDER BY O.""DocEntry""";

                using (var command = new OdbcCommand(headerQuery, connection))
                {
                    if (since.HasValue) command.Parameters.AddWithValue("@Since", since.Value.Date);
                    using var reader = await command.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var docEntry = Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture);
                        var status = MapSalesOrderStatus(
                            ParseErpText(reader.IsDBNull(4) ? null : reader.GetValue(4)),
                            ParseErpText(reader.IsDBNull(5) ? null : reader.GetValue(5)));
                        orders[docEntry] = new ErpSalesOrderDto
                        {
                            DocEntry = docEntry,
                            DocNum = ReadNullableInt(reader, 1),
                            CardCode = ParseErpText(reader.IsDBNull(2) ? null : reader.GetValue(2)),
                            DocDate = reader.IsDBNull(3) ? null : reader.GetDateTime(3),
                            Status = status,
                            DocTotal = ReadNullableDecimal(reader, 6) ?? 0m,
                            CorrelationId = ParseErpText(reader.IsDBNull(7) ? null : reader.GetValue(7)) ?? string.Empty,
                            // Un ordine chiuso o annullato non ha residuo, qualunque cosa resti sulle righe.
                            OpenAmount = status == "open" ? ReadNullableDecimal(reader, 8) ?? 0m : 0m,
                        };
                    }
                }

                if (orders.Count == 0) return Array.Empty<ErpSalesOrderDto>();

                // Fatture tratte dall'ordine: righe INV1 con BaseType 17 (ordine cliente). Le fatture annullate
                // (e i loro documenti di annullamento) non contano: dopo l'annullamento l'ordine si riapre.
                var invoiceQuery = $@"
                    SELECT DISTINCT L.""BaseEntry"", H.""DocEntry"", H.""DocNum"", H.""DocDate""
                    FROM ""{_schema}"".""INV1"" L
                    INNER JOIN ""{_schema}"".""OINV"" H ON H.""DocEntry"" = L.""DocEntry""
                    INNER JOIN ""{_schema}"".""ORDR"" O ON O.""DocEntry"" = L.""BaseEntry""
                    WHERE L.""BaseType"" = 17 AND H.""CANCELED"" = 'N'
                      AND IFNULL(O.""{udf}"", '') <> ''{sinceClause}
                    ORDER BY L.""BaseEntry"", H.""DocEntry""";

                using (var command = new OdbcCommand(invoiceQuery, connection))
                {
                    if (since.HasValue) command.Parameters.AddWithValue("@Since", since.Value.Date);
                    using var reader = await command.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var baseEntry = ReadNullableInt(reader, 0);
                        if (baseEntry == null || !orders.TryGetValue(baseEntry.Value, out var order)) continue;
                        order.Invoices.Add(new ErpSalesOrderInvoiceDto
                        {
                            DocEntry = Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture),
                            DocNum = ReadNullableInt(reader, 2),
                            DocDate = reader.IsDBNull(3) ? null : reader.GetDateTime(3),
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Errore nella lettura degli ordini cliente correlati (since={Since})", since);
                throw;
            }

            return orders.Values.ToList();
        }

        /// <summary>
        /// Stato ERP-neutro di un ordine cliente. <c>CANCELED</c> vince sempre: un ordine annullato ha anche
        /// <c>DocStatus = 'C'</c>, ma per il portale "annullato" e "evaso" sono cose opposte.
        /// </summary>
        internal static string MapSalesOrderStatus(string? docStatus, string? canceled)
            => canceled is "Y" or "C" ? "cancelled"
             : docStatus == "C" ? "closed"
             : "open";

        /// <summary>
        /// Articoli per le righe dei documenti di vendita. Con <paramref name="sellableOnly"/> solo gli articoli
        /// di vendita (<c>SellItem = 'Y'</c>) ATTIVI oggi; altrimenti tutti, con il flag <c>active</c>.
        /// <para><b>Perché l'attività si calcola qui e non con <c>validFor = 'Y'</c>.</b> In SAP B1 "inattivo" è
        /// <c>frozenFor = 'Y'</c> (eventualmente in un intervallo <c>frozenFrom..frozenTo</c>), mentre
        /// <c>validFor = 'Y'</c> significa "attivo SOLO nell'intervallo <c>validFrom..validTo</c>". Esistono
        /// articoli con entrambi a 'N', che per SAP sono attivi senza limiti: un filtro <c>validFor = 'Y'</c> li
        /// escluderebbe. La regola completa sta in <see cref="IsItemActive"/>.</para>
        /// </summary>
        public async Task<IEnumerable<ErpItemDto>> GetSellableItemsAsync(bool sellableOnly)
        {
            var items = new List<ErpItemDto>();
            var today = DateTime.Today;
            try
            {
                using var connection = await CreateOpenConnectionAsync();
                var query = $@"
                    SELECT I.""ItemCode"", I.""ItemName"", G.""ItmsGrpNam"", I.""VatGourpSa"",
                           I.""validFor"", I.""validFrom"", I.""validTo"",
                           I.""frozenFor"", I.""frozenFrom"", I.""frozenTo""
                    FROM ""{_schema}"".""OITM"" I
                    LEFT JOIN ""{_schema}"".""OITB"" G ON G.""ItmsGrpCod"" = I.""ItmsGrpCod""
                    {(sellableOnly ? @"WHERE I.""SellItem"" = 'Y'" : string.Empty)}
                    ORDER BY I.""ItemCode""";

                using var command = new OdbcCommand(query, connection);
                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var active = IsItemActive(
                        ParseErpText(reader.IsDBNull(4) ? null : reader.GetValue(4)),
                        reader.IsDBNull(5) ? null : reader.GetDateTime(5),
                        reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                        ParseErpText(reader.IsDBNull(7) ? null : reader.GetValue(7)),
                        reader.IsDBNull(8) ? null : reader.GetDateTime(8),
                        reader.IsDBNull(9) ? null : reader.GetDateTime(9),
                        today);
                    if (sellableOnly && !active) continue;

                    items.Add(new ErpItemDto
                    {
                        ItemCode = ParseErpText(reader.IsDBNull(0) ? null : reader.GetValue(0)) ?? string.Empty,
                        ItemName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1).Trim(),
                        GroupName = ParseErpText(reader.IsDBNull(2) ? null : reader.GetValue(2)),
                        SalesVatGroup = ParseErpText(reader.IsDBNull(3) ? null : reader.GetValue(3)),
                        Active = active,
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Errore nella lettura degli articoli (sellableOnly={SellableOnly})", sellableOnly);
                throw;
            }
            return items;
        }

        /// <summary>
        /// Stato attivo di un articolo SAP B1 alla data <paramref name="today"/>:
        /// <list type="bullet">
        /// <item>inattivo se <c>frozenFor = 'Y'</c> e la data cade in <c>frozenFrom..frozenTo</c> (estremi nulli = aperti);</item>
        /// <item>inattivo se <c>validFor = 'Y'</c> e la data cade FUORI da <c>validFrom..validTo</c>;</item>
        /// <item>attivo in tutti gli altri casi, compreso <c>validFor = frozenFor = 'N'</c>.</item>
        /// </list>
        /// </summary>
        internal static bool IsItemActive(
            string? validFor, DateTime? validFrom, DateTime? validTo,
            string? frozenFor, DateTime? frozenFrom, DateTime? frozenTo,
            DateTime today)
        {
            var d = today.Date;
            if (frozenFor == "Y"
                && (frozenFrom == null || frozenFrom.Value.Date <= d)
                && (frozenTo == null || frozenTo.Value.Date >= d))
                return false;

            if (validFor == "Y"
                && ((validFrom != null && validFrom.Value.Date > d) || (validTo != null && validTo.Value.Date < d)))
                return false;

            return true;
        }

        /// <summary>Codici OPRJ che rispondono al pattern LIKE indicato (con <c>ESCAPE '\'</c>).</summary>
        public async Task<IReadOnlyList<string>> GetFiscalProjectCodesLikeAsync(string likePattern)
        {
            var codes = new List<string>();
            using var connection = await CreateOpenConnectionAsync();
            using var command = new OdbcCommand(
                $@"SELECT ""PrjCode"" FROM ""{_schema}"".""OPRJ"" WHERE ""PrjCode"" LIKE ? ESCAPE '\'",
                connection);
            command.Parameters.AddWithValue("@Like", likePattern);
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var code = ParseErpText(reader.IsDBNull(0) ? null : reader.GetValue(0));
                if (code != null) codes.Add(code);
            }
            return codes;
        }

        public async Task<bool> FiscalProjectExistsAsync(string code)
        {
            using var connection = await CreateOpenConnectionAsync();
            using var command = new OdbcCommand($@"SELECT COUNT(*) FROM ""{_schema}"".""OPRJ"" WHERE ""PrjCode"" = ?", connection);
            command.Parameters.AddWithValue("@Code", code);
            var count = await command.ExecuteScalarAsync();
            return count != null && count != DBNull.Value && Convert.ToInt32(count, CultureInfo.InvariantCulture) > 0;
        }

        /// <summary>
        /// Allegati (<c>ATC1</c>) con uno dei nomi di file indicati, senza estensione come li salva SAP. Sola
        /// lettura: serve a riusare la voce caricata da un tentativo precedente dello stesso documento (vedi
        /// <see cref="SalesDocumentPayloadBuilder.SelectReusableAttachmentEntry"/>).
        /// </summary>
        public async Task<IReadOnlyList<AttachmentFileRow>> FindAttachmentsByFileNamesAsync(IReadOnlyCollection<string> fileNames)
        {
            if (fileNames.Count == 0) return Array.Empty<AttachmentFileRow>();
            using var connection = await CreateOpenConnectionAsync();
            using var command = new OdbcCommand(
                $@"SELECT ""AbsEntry"", ""FileName"", ""FileExt"" FROM ""{_schema}"".""ATC1"" WHERE ""FileName"" IN ({Placeholders(fileNames.Count)})",
                connection);
            foreach (var name in fileNames) command.Parameters.AddWithValue("@FileName", name);

            var rows = new List<AttachmentFileRow>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var entry = ReadNullableInt(reader, 0);
                var fileName = ParseErpText(reader.IsDBNull(1) ? null : reader.GetValue(1));
                if (entry == null || fileName == null) continue;
                rows.Add(new AttachmentFileRow(entry.Value, fileName, ParseErpText(reader.IsDBNull(2) ? null : reader.GetValue(2))));
            }
            return rows;
        }

        private static decimal? ReadNullableDecimal(System.Data.Common.DbDataReader reader, int ordinal)
        {
            if (reader.IsDBNull(ordinal)) return null;
            var raw = reader.GetValue(ordinal);
            return raw switch
            {
                decimal d => d,
                double dbl => (decimal)dbl,
                float f => (decimal)f,
                int i => i,
                long l => l,
                short s => s,
                _ => decimal.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture)?.Trim(),
                        NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
                     ? parsed
                     : null,
            };
        }
    }
}
