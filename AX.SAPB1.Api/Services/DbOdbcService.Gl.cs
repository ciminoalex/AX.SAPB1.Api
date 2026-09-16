using System.Data.Odbc;
using AX.SAPB1.Api.Models;

namespace AX.SAPB1.Api.Services
{
    /// <summary>
    /// Contabilità generale: lettura del conto economico e scrittura dell'attribuzione analitica
    /// (progetto + dimensioni) sulle righe contabili.
    ///
    /// <para><b>Perché ODBC diretto e non Service Layer.</b> Progetto e centri di costo non entrano in
    /// nessun saldo: non toccano conti né importi, sono una sovrastruttura di interrogazione. Il bilancio,
    /// la trial balance e le dichiarazioni non li vedono. È la prassi in uso su questo impianto da anni.
    /// Verificato sullo schema: zero trigger, zero foreign key, zero constraint CHECK su JDT1/OJDT.</para>
    ///
    /// <para><b>Cosa NON si aggiorna, e perché.</b> <c>OJDT."Project"</c> (testata) è valorizzato su 10
    /// righe su 51.345: non è un rollup delle righe ma un default di input che SAP non mantiene — 2.863
    /// registrazioni hanno già la testata vuota con le righe piene. Scriverlo renderebbe l'impianto meno
    /// uniforme al comportamento nativo, e sulle registrazioni multi-progetto non esisterebbe un valore
    /// corretto da metterci. Le righe documento (INV1/PCH1) divergono già dalla scrittura contabile:
    /// SAP copia una volta sola alla registrazione e non riallinea.</para>
    /// </summary>
    public partial class DbOdbcService
    {
        // Conto economico = GroupMask 4..8. Le scritture di apertura/chiusura ('-2'/'-3') sono escluse
        // sempre: toccano i conti CE e raddoppierebbero ogni somma. TransType è NVARCHAR(20) su questo
        // impianto (verificato), quindi il confronto va fatto con le stringhe.
        private const string CeGroupMaskFilter = @"A.""GroupMask"" IN (4,5,6,7,8)";
        private const string NoOpenCloseFilter = @"J.""TransType"" NOT IN ('-2','-3')";

        // Cap difensivo sulla paginazione di /api/gl/lines. Condiviso con GlController (stesso assembly,
        // referenziato come DbOdbcService.MaxGlLinesTake): il servizio deve essere corretto a prescindere
        // dal chiamante, quindi non basta il cap nel controller.
        internal const int MaxGlLinesTake = 5000;

        private int WriteCommandTimeoutSeconds =>
            int.TryParse(_configuration["SapB1:Write:CommandTimeoutSeconds"], out var v) && v > 0 ? v : 120;

        private int WriteMaxBatchSize =>
            int.TryParse(_configuration["SapB1:Write:MaxBatchSize"], out var v) && v > 0 ? v : 200;

        /// <summary>
        /// Il write path è <b>opt-in esplicito</b>: chiave assente o non parsabile ⇒ disattivato.
        /// <para>
        /// Il default conta più di quanto sembri. <c>deploy.ps1</c> non copia <c>appsettings.json</c>, quindi
        /// un server che non è ancora stato configurato non ha affatto la sezione <c>SapB1:Write</c>. Con un
        /// default permissivo, il primo deploy accenderebbe la scrittura su dati contabili di produzione
        /// per pura assenza di configurazione — e nessuno lo saprebbe. Chi vuole scrivere lo dichiara.
        /// </para>
        /// </summary>
        private bool WriteEnabled =>
            bool.TryParse(_configuration["SapB1:Write:Enabled"], out var v) && v;

        // ─────────────────────────────────────────────────────────────────────
        // Lettura
        // ─────────────────────────────────────────────────────────────────────

        public async Task<IEnumerable<GlAccountDto>> GetGlAccountsAsync()
        {
            var result = new List<GlAccountDto>();
            using var connection = await CreateOpenConnectionAsync();
            var query = $@"
                SELECT ""AcctCode"", ""AcctName"", ""FatherNum"", ""Postable"", ""GroupMask""
                FROM ""{_schema}"".""OACT""
                WHERE ""GroupMask"" IN (4,5,6,7,8)
                ORDER BY ""AcctCode""";
            using var command = new OdbcCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new GlAccountDto
                {
                    ErpAccountCode = reader.GetString(0),
                    Name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    ParentCode = reader.IsDBNull(2) ? null : reader.GetString(2),
                    IsPostable = !reader.IsDBNull(3) && reader.GetString(3) == "Y",
                    IsIncomeStatement = true,
                    StatementSection = MapStatementSection(reader.IsDBNull(4) ? 0 : Convert.ToInt32(reader.GetValue(4))),
                });
            }
            return result;
        }

        public async Task<IEnumerable<GlFiscalPeriodDto>> GetGlFiscalPeriodsAsync()
        {
            var result = new List<GlFiscalPeriodDto>();
            using var connection = await CreateOpenConnectionAsync();
            var query = $@"
                SELECT ""Code"", ""F_RefDate"", ""T_RefDate"", ""Indicator"", ""SubNum"", ""PeriodStat""
                FROM ""{_schema}"".""OFPR""
                WHERE ""Indicator"" IS NOT NULL
                ORDER BY ""F_RefDate""";
            using var command = new OdbcCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                // Indicator = anno fiscale. Su questo impianto è sempre l'anno solare, ma esiste un set
                // legacy "Standard" non numerico che va scartato invece di crashare.
                var indicator = reader.IsDBNull(3) ? null : reader.GetString(3);
                if (!int.TryParse(indicator, out var fiscalYear)) continue;

                result.Add(new GlFiscalPeriodDto
                {
                    Code = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                    FiscalYear = fiscalYear,
                    PeriodNumber = reader.IsDBNull(4) ? 0 : Convert.ToInt32(reader.GetValue(4)),
                    StartDate = reader.IsDBNull(1) ? default : reader.GetDateTime(1),
                    EndDate = reader.IsDBNull(2) ? default : reader.GetDateTime(2),
                    // 'N' = aperto, 'C' = chiuso. Irrilevante per la scrittura ODBC (che non passa dalla
                    // business logic), ma il portale lo mostra: un anno chiuso ha numeri definitivi.
                    IsOpen = reader.IsDBNull(5) || reader.GetString(5) == "N",
                });
            }
            return result;
        }

        public async Task<IEnumerable<GlFiscalProjectDto>> GetGlFiscalProjectsAsync()
        {
            var result = new List<GlFiscalProjectDto>();
            using var connection = await CreateOpenConnectionAsync();
            var query = $@"
                SELECT ""PrjCode"", ""PrjName"", ""Active""
                FROM ""{_schema}"".""OPRJ""
                ORDER BY ""PrjCode""";
            using var command = new OdbcCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new GlFiscalProjectDto
                {
                    Code = reader.GetString(0),
                    Name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    IsActive = reader.IsDBNull(2) || reader.GetString(2) == "Y",
                });
            }
            return result;
        }

        public async Task<IEnumerable<GlDimensionDto>> GetGlDimensionsAsync()
        {
            var result = new List<GlDimensionDto>();
            using var connection = await CreateOpenConnectionAsync();
            var query = $@"
                SELECT ""DimCode"", ""DimDesc"", ""DimActive""
                FROM ""{_schema}"".""ODIM""
                ORDER BY ""DimCode""";
            using var command = new OdbcCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var num = reader.IsDBNull(0) ? 0 : Convert.ToInt32(reader.GetValue(0));
                result.Add(new GlDimensionDto
                {
                    Number = num,
                    Code = num.ToString(),
                    Name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    IsActive = !reader.IsDBNull(2) && reader.GetString(2) == "Y",
                });
            }
            return result;
        }

        public async Task<IEnumerable<GlDistributionRuleDto>> GetGlDistributionRulesAsync()
        {
            var result = new List<GlDistributionRuleDto>();
            using var connection = await CreateOpenConnectionAsync();
            // OOCR è l'anagrafica dei codici che finiscono su JDT1.ProfitCode/OcrCodeN — verificato
            // empiricamente contro OPRC, che è una lista diversa e solo parzialmente sovrapposta.
            var query = $@"
                SELECT ""OcrCode"", ""OcrName"", ""DimCode"", ""Active""
                FROM ""{_schema}"".""OOCR""
                ORDER BY ""DimCode"", ""OcrName""";
            using var command = new OdbcCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new GlDistributionRuleDto
                {
                    Code = reader.GetString(0),
                    Name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    DimensionNumber = reader.IsDBNull(2) ? 0 : Convert.ToInt32(reader.GetValue(2)),
                    IsActive = !reader.IsDBNull(3) && reader.GetString(3) == "Y",
                });
            }
            return result;
        }

        /// <summary>
        /// Righe di conto economico nella finestra [from, to] per data di registrazione.
        /// <para>
        /// Nessun watermark incrementale, e non è una svista. Le registrazioni vengono modificate in place
        /// su questo impianto (3-5% l'anno, <c>StornoDate</c> sempre NULL: non si storna, si riscrive), e
        /// il nostro stesso UPDATE non avanza <c>OJDT.UpdateDate</c>. Un <c>since</c> su RefDate perderebbe
        /// i ratei retroattivi — che sono il 65% del lavoro — e nessun <c>since</c> vedrebbe le
        /// cancellazioni. Con 3.400-5.500 righe l'anno la rilettura integrale della finestra costa nulla
        /// ed è esatta; il portale la completa con un mark-and-sweep.
        /// </para>
        ///
        /// <para><b>Paginazione.</b> Con <paramref name="take"/> &gt; 0 la query restituisce una
        /// finestra di <c>take</c> righe a partire da <c>skip</c>. L'ORDER BY è già totale
        /// (RefDate, TransId, Line_ID è unico), quindi le pagine non si sovrappongono e non
        /// perdono righe. <b>Il chiamante deve leggere TUTTE le pagine prima dello sweep</b>:
        /// fermarsi a metà farebbe cancellare dal mirror le righe non ancora lette.</para>
        /// </summary>
        public async Task<IEnumerable<GlLineDto>> GetGlLinesAsync(DateTime from, DateTime to, int skip = 0, int take = 0)
        {
            // Difesa nel servizio, non solo nel controller: il servizio deve essere corretto a
            // prescindere da chi lo chiama.
            skip = Math.Max(0, skip);
            take = Math.Clamp(take, 0, MaxGlLinesTake);

            var result = new List<GlLineDto>();
            using var connection = await CreateOpenConnectionAsync();

            // La controparte si risolve dal documento d'origine. Un LEFT JOIN per tipo: i journal manuali
            // (il 65% delle righe) non ne hanno alcuna, ed è corretto che restino senza.
            // Il progetto di TESTATA documento viaggia a parte e solo come ripiego: sulle fatture 2026 il
            // codice è spesso scritto solo lì e JDT1."Project" resta vuoto. Non si usa OJDT."Project" (SAP non
            // lo mantiene, vedi sopra) e non sostituisce mai il progetto di riga, che resta l'autorevole.
            var query = $@"
                SELECT J.""TransId"", J.""Line_ID"", J.""Account"", A.""AcctName"", A.""GroupMask"",
                       J.""RefDate"", J.""DueDate"", H.""TransType"", J.""BaseRef"",
                       J.""Project"", J.""ProfitCode"", J.""OcrCode2"", J.""OcrCode3"",
                       J.""Debit"", J.""Credit"", J.""LineMemo"",
                       COALESCE(OI.""CardCode"", OP.""CardCode"", RI.""CardCode"", RP.""CardCode"") AS ""CardCode"",
                       COALESCE(OI.""CardName"", OP.""CardName"", RI.""CardName"", RP.""CardName"") AS ""CardName"",
                       COALESCE(NULLIF(OI.""Project"",''), NULLIF(OP.""Project"",''), NULLIF(RI.""Project"",''), NULLIF(RP.""Project"",'')) AS ""HeaderProjectCode"",
                       W.""WorkProjectCode""
                FROM ""{_schema}"".""JDT1"" J
                INNER JOIN ""{_schema}"".""OACT"" A ON A.""AcctCode"" = J.""Account""
                INNER JOIN ""{_schema}"".""OJDT"" H ON H.""TransId"" = J.""TransId""
                LEFT JOIN ""{_schema}"".""OINV"" OI ON OI.""TransId"" = J.""TransId""
                LEFT JOIN ""{_schema}"".""OPCH"" OP ON OP.""TransId"" = J.""TransId""
                LEFT JOIN ""{_schema}"".""ORIN"" RI ON RI.""TransId"" = J.""TransId""
                LEFT JOIN ""{_schema}"".""ORPC"" RP ON RP.""TransId"" = J.""TransId""
                {WorkProjectJoin}
                WHERE {CeGroupMaskFilter} AND {NoOpenCloseFilter}
                  AND J.""RefDate"" >= ? AND J.""RefDate"" <= ?
                ORDER BY J.""RefDate"", J.""TransId"", J.""Line_ID""";
            // LIMIT/OFFSET come letterali interi, non come parametri bind (? ?). Questo è l'unico uso di
            // LIMIT/OFFSET in tutto il repo: non c'è un precedente che dimostri che HDBODBC accetti un
            // marcatore di parametro lì, e il percorso HTTP→ODBC→HANA non è verificabile da ogni ambiente
            // di sviluppo. Molti driver ODBC vogliono valori letterali in questa clausola. Concatenare
            // valori in una query è normalmente vietato, e qui resta legittimo solo perché skip/take sono
            // int (non stringhe) e arrivano già clampati sopra (Math.Max/Math.Clamp): non c'è alcuna
            // superficie di injection, e lasciarli come parametri lascerebbe aperta una domanda sul driver
            // a cui nessuno può rispondere prima della produzione.
            if (take > 0) query += $"\n                    LIMIT {take} OFFSET {skip}";

            using var command = new OdbcCommand(query, connection);
            command.CommandTimeout = WriteCommandTimeoutSeconds;
            command.Parameters.AddWithValue("@From", from.Date);
            command.Parameters.AddWithValue("@To", to.Date);

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var transType = reader.IsDBNull(7) ? null : reader.GetString(7);
                var cardCode = reader.IsDBNull(16) ? null : reader.GetString(16);
                var postingDate = reader.IsDBNull(5) ? default : reader.GetDateTime(5);

                result.Add(new GlLineDto
                {
                    ErpEntryId = Convert.ToInt32(reader.GetValue(0)),
                    ErpLineId = Convert.ToInt32(reader.GetValue(1)),
                    ErpAccountCode = reader.GetString(2),
                    ErpAccountName = reader.IsDBNull(3) ? null : reader.GetString(3),
                    StatementSection = MapStatementSection(reader.IsDBNull(4) ? 0 : Convert.ToInt32(reader.GetValue(4))),
                    PostingDate = DateTime.SpecifyKind(postingDate, DateTimeKind.Utc),
                    DueDate = reader.IsDBNull(6) ? null : DateTime.SpecifyKind(reader.GetDateTime(6), DateTimeKind.Utc),
                    FiscalYear = postingDate.Year,
                    SourceDocType = MapSourceDocType(transType),
                    SourceDocNumber = reader.IsDBNull(8) ? null : reader.GetString(8),
                    ErpProjectCode = NullIfEmpty(reader, 9),
                    HeaderProjectCode = NullIfEmpty(reader, 18),
                    WorkProjectCode = NullIfEmpty(reader, 19),
                    Dimension1Code = NullIfEmpty(reader, 10),
                    Dimension2Code = NullIfEmpty(reader, 11),
                    Dimension3Code = NullIfEmpty(reader, 12),
                    Debit = reader.IsDBNull(13) ? 0m : reader.GetDecimal(13),
                    Credit = reader.IsDBNull(14) ? 0m : reader.GetDecimal(14),
                    Currency = "EUR",
                    Memo = reader.IsDBNull(15) ? null : reader.GetString(15),
                    ErpCounterpartyCode = cardCode,
                    ErpCounterpartyName = reader.IsDBNull(17) ? null : reader.GetString(17),
                    ErpCounterpartyType = MapCounterpartyType(transType, cardCode),
                });
            }
            return result;
        }

        /// <summary>
        /// LEFT JOIN che porta sulla riga contabile il codice progetto <b>contabile</b> del lavoro fatturato,
        /// ricavato dai timesheet collegati alle righe del documento d'origine: <c>INV1/RIN1."U_SGS_PRJ_TmsCode"</c>
        /// → <c>@SGS_PRJ_OTMS."U_Project"</c> (= <c>OPMG."AbsEntry"</c>) → <c>OPMG."FIPROJECT"</c>.
        /// <para>Esiste perché le fatture T&amp;M generate dal tool di gestione progetto dicono, riga per riga,
        /// quale timesheet fatturano: è un'informazione più precisa del codice di testata (misurato il
        /// 14/09/2026: su 89 righe 2026 con entrambi, 11 testate indicavano il T&amp;M di un altro cliente o di
        /// un'altra commessa), e sulle fatture 2025 la testata è quasi sempre vuota.</para>
        /// <para>Il codice si dà <b>solo se univoco</b>: per (registrazione, conto) TUTTE le righe del documento
        /// devono avere un timesheet con un progetto che ha il codice contabile, e il codice dev'essere uno
        /// solo (<c>MIN &lt;&gt; ''</c> e <c>COUNT DISTINCT = 1</c>). Una fattura mista o parziale non propone
        /// nulla: meglio una riga da attribuire a mano che una attribuita a metà. Solo fatture e note di credito
        /// di vendita; sola lettura; non sostituisce mai il progetto di riga (lo decide il portale).</para>
        /// </summary>
        private string WorkProjectJoin => $@"LEFT JOIN (
                    SELECT D.""TransId"", D.""AcctCode"", MAX(D.""FP"") AS ""WorkProjectCode""
                    FROM (
                        SELECT DH.""TransId"", DL.""AcctCode"", IFNULL(DP.""FIPROJECT"", '') AS ""FP""
                        FROM ""{_schema}"".""OINV"" DH
                        INNER JOIN ""{_schema}"".""INV1"" DL ON DL.""DocEntry"" = DH.""DocEntry""
                        LEFT JOIN ""{_schema}"".""@SGS_PRJ_OTMS"" DT ON DT.""Code"" = DL.""U_SGS_PRJ_TmsCode""
                        LEFT JOIN ""{_schema}"".""OPMG"" DP ON TO_NVARCHAR(DP.""AbsEntry"") = DT.""U_Project""
                        UNION ALL
                        SELECT DH.""TransId"", DL.""AcctCode"", IFNULL(DP.""FIPROJECT"", '') AS ""FP""
                        FROM ""{_schema}"".""ORIN"" DH
                        INNER JOIN ""{_schema}"".""RIN1"" DL ON DL.""DocEntry"" = DH.""DocEntry""
                        LEFT JOIN ""{_schema}"".""@SGS_PRJ_OTMS"" DT ON DT.""Code"" = DL.""U_SGS_PRJ_TmsCode""
                        LEFT JOIN ""{_schema}"".""OPMG"" DP ON TO_NVARCHAR(DP.""AbsEntry"") = DT.""U_Project""
                    ) D
                    GROUP BY D.""TransId"", D.""AcctCode""
                    HAVING MIN(D.""FP"") <> '' AND COUNT(DISTINCT D.""FP"") = 1
                ) W ON W.""TransId"" = J.""TransId"" AND W.""AcctCode"" = J.""Account""";

        /// <summary>Re-sync mirato dopo una scrittura: chiude il cerchio senza rileggere l'intera finestra.</summary>
        public async Task<IEnumerable<GlLineDto>> GetGlLinesByEntryIdsAsync(IReadOnlyCollection<int> entryIds)
        {
            if (entryIds.Count == 0) return Array.Empty<GlLineDto>();

            var result = new List<GlLineDto>();
            using var connection = await CreateOpenConnectionAsync();

            // Placeholder generati contando gli input: mai concatenare i valori nel testo SQL.
            var placeholders = string.Join(",", entryIds.Select(_ => "?"));
            // Stessa testata-ripiego della lettura per finestra: chi rilegge dopo una scrittura deve vedere
            // la riga come la vedrebbe il sync completo, altrimenti il ripiego sparirebbe fino al giro dopo.
            var query = $@"
                SELECT J.""TransId"", J.""Line_ID"", J.""Project"", J.""ProfitCode"", J.""OcrCode2"", J.""OcrCode3"",
                       COALESCE(NULLIF(OI.""Project"",''), NULLIF(OP.""Project"",''), NULLIF(RI.""Project"",''), NULLIF(RP.""Project"",'')) AS ""HeaderProjectCode"",
                       W.""WorkProjectCode""
                FROM ""{_schema}"".""JDT1"" J
                LEFT JOIN ""{_schema}"".""OINV"" OI ON OI.""TransId"" = J.""TransId""
                LEFT JOIN ""{_schema}"".""OPCH"" OP ON OP.""TransId"" = J.""TransId""
                LEFT JOIN ""{_schema}"".""ORIN"" RI ON RI.""TransId"" = J.""TransId""
                LEFT JOIN ""{_schema}"".""ORPC"" RP ON RP.""TransId"" = J.""TransId""
                {WorkProjectJoin}
                WHERE J.""TransId"" IN ({placeholders})
                ORDER BY J.""TransId"", J.""Line_ID""";

            using var command = new OdbcCommand(query, connection);
            command.CommandTimeout = WriteCommandTimeoutSeconds;
            foreach (var id in entryIds) command.Parameters.AddWithValue("@Id", id);

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new GlLineDto
                {
                    ErpEntryId = Convert.ToInt32(reader.GetValue(0)),
                    ErpLineId = Convert.ToInt32(reader.GetValue(1)),
                    ErpProjectCode = NullIfEmpty(reader, 2),
                    HeaderProjectCode = NullIfEmpty(reader, 6),
                    WorkProjectCode = NullIfEmpty(reader, 7),
                    Dimension1Code = NullIfEmpty(reader, 3),
                    Dimension2Code = NullIfEmpty(reader, 4),
                    Dimension3Code = NullIfEmpty(reader, 5),
                });
            }
            return result;
        }

        /// <summary>
        /// Fattura (o nota di credito) di ORIGINE di una registrazione JDT1, per l'anteprima nella
        /// riconciliazione: dalla descrizione del conto non sempre si capisce cosa si è comprato o venduto,
        /// le RIGHE del documento sì. Chiave robusta e univoca: <c>OINV/OPCH/ORIN/ORPC."TransId"</c> =
        /// <c>OJDT."TransId"</c> (= l'id registrazione del portale). Sola lettura. Ritorna null se il tipo
        /// non ha un documento d'origine con righe (giornali manuali, pagamenti) o se non c'è testata.
        /// </summary>
        public async Task<ErpInvoiceDto?> GetSourceDocumentAsync(int transId, string? docType)
        {
            var tables = MapSourceDocTables(docType);
            if (tables is null) return null;
            var (headerTable, lineTable) = tables.Value;

            using var connection = await CreateOpenConnectionAsync();

            var invoice = new ErpInvoiceDto
            {
                DocType = docType!,
                IsCreditNote = docType is GlSourceDocType.SalesCreditNote or GlSourceDocType.PurchaseCreditNote,
            };
            int docEntry;

            // Testata via TransId (univoco su tutto il giornale). Le 4 tabelle condividono queste colonne.
            var headerSql = $@"
                SELECT ""DocEntry"", ""DocNum"", ""CardCode"", ""DocDate"", ""DocCur"", ""DocTotal"", ""VatSum""
                FROM ""{_schema}"".""{headerTable}""
                WHERE ""TransId"" = ?";
            using (var headerCmd = new OdbcCommand(headerSql, connection))
            {
                headerCmd.Parameters.AddWithValue("@TransId", transId);
                using var hr = await headerCmd.ExecuteReaderAsync();
                if (!await hr.ReadAsync()) return null;   // nessun documento d'origine per questo TransId
                docEntry = Convert.ToInt32(hr.GetValue(0));
                invoice.ErpDocId = docEntry.ToString();
                invoice.ErpDocNumber = hr.IsDBNull(1) ? string.Empty : (Convert.ToString(hr.GetValue(1)) ?? string.Empty);
                invoice.ErpCustomerCode = hr.IsDBNull(2) ? null : hr.GetString(2);
                invoice.IssueDate = hr.IsDBNull(3) ? null : hr.GetDateTime(3);
                invoice.Currency = hr.IsDBNull(4) || hr.GetString(4).Length == 0 ? "EUR" : hr.GetString(4);
                invoice.TotalAmount = hr.IsDBNull(5) ? 0m : hr.GetDecimal(5);
                invoice.VatAmount = hr.IsDBNull(6) ? 0m : hr.GetDecimal(6);
                invoice.TaxableAmount = invoice.TotalAmount - invoice.VatAmount;
            }

            // Righe (prodotti/servizi) via DocEntry. INV1/PCH1/RIN1/RPC1 condividono queste colonne.
            var lineSql = $@"
                SELECT ""LineNum"", ""ItemCode"", ""Dscription"", ""Quantity"", ""Price"", ""LineTotal"", ""VatPrcnt""
                FROM ""{_schema}"".""{lineTable}""
                WHERE ""DocEntry"" = ?
                ORDER BY ""LineNum""";
            using (var lineCmd = new OdbcCommand(lineSql, connection))
            {
                lineCmd.Parameters.AddWithValue("@DocEntry", docEntry);
                using var lr = await lineCmd.ExecuteReaderAsync();
                while (await lr.ReadAsync())
                {
                    invoice.Lines.Add(new ErpInvoiceLineDto
                    {
                        SortOrder = lr.IsDBNull(0) ? 0 : Convert.ToInt32(lr.GetValue(0)),
                        ErpItemCode = lr.IsDBNull(1) || lr.GetString(1).Length == 0 ? null : lr.GetString(1),
                        Description = lr.IsDBNull(2) ? string.Empty : lr.GetString(2),
                        Quantity = lr.IsDBNull(3) ? 0m : lr.GetDecimal(3),
                        UnitPrice = lr.IsDBNull(4) ? 0m : lr.GetDecimal(4),
                        LineTotal = lr.IsDBNull(5) ? 0m : lr.GetDecimal(5),
                        VatRate = lr.IsDBNull(6) ? 0m : lr.GetDecimal(6),
                    });
                }
            }

            return invoice;
        }

        /// <summary>Tipo documento neutro → (testata, righe) SAP. Solo fatture e note di credito hanno righe da mostrare.</summary>
        private static (string header, string line)? MapSourceDocTables(string? docType) => docType switch
        {
            GlSourceDocType.SalesInvoice => ("OINV", "INV1"),
            GlSourceDocType.PurchaseInvoice => ("OPCH", "PCH1"),
            GlSourceDocType.SalesCreditNote => ("ORIN", "RIN1"),
            GlSourceDocType.PurchaseCreditNote => ("ORPC", "RPC1"),
            _ => null,
        };

        // ─────────────────────────────────────────────────────────────────────
        // Scrittura
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Aggiorna progetto e dimensioni su una o più righe contabili, in <b>una sola transazione</b>:
        /// o passano tutte o non passa nessuna.
        /// <para>
        /// Presidi, tutti necessari e nessuno decorativo: (1) i codici sono validati contro OPRJ/OOCR
        /// <i>prima</i> di aprire la transazione — nel DB non esiste una sola foreign key, quindi questo è
        /// l'unico controllo che esiste; (2) la WHERE è fissa sulla PK e la SET list è costante, così
        /// l'ordine dei parametri posizionali di ODBC non può scivolare; (3) il compare-and-swap intercetta
        /// chi ha modificato la riga dal client SAP nel frattempo; (4) <c>CommandTimeout</c> esplicito,
        /// perché il default di 30s non regge la latenza della VPN; (5) i valori tornano <b>riletti dal
        /// DB</b>, non echeggiati.
        /// </para>
        /// </summary>
        public async Task<GlAttributionResult> UpdateGlAttributionAsync(GlAttributionRequest request)
        {
            var result = new GlAttributionResult();

            if (!WriteEnabled)
            {
                result.ErrorMessage = "Scrittura verso l'ERP disattivata (SapB1:Write:Enabled=false).";
                return result;
            }
            if (request.Items.Count == 0)
            {
                result.Success = true;
                return result;
            }
            if (request.Items.Count > WriteMaxBatchSize)
            {
                result.ErrorMessage = $"Batch di {request.Items.Count} righe: il massimo consentito è {WriteMaxBatchSize}.";
                return result;
            }

            using var connection = await CreateOpenConnectionAsync();

            // Validazione dell'intero batch in due query, fuori dalla transazione: se un codice non esiste
            // la richiesta è respinta intera e nulla viene scritto.
            var invalid = await FindInvalidCodesAsync(connection, request.Items);
            if (invalid.Count > 0)
            {
                result.InvalidCodes = invalid;
                result.ErrorMessage = "Codici inesistenti in anagrafica: " + string.Join(", ", invalid);
                return result;
            }

            using var transaction = connection.BeginTransaction();
            try
            {
                var updateSql = $@"
                    UPDATE ""{_schema}"".""JDT1""
                       SET ""Project"" = ?, ""ProfitCode"" = ?, ""OcrCode2"" = ?, ""OcrCode3"" = ?
                     WHERE ""TransId"" = ? AND ""Line_ID"" = ?
                       AND IFNULL(""Project"", '') = ?
                       AND IFNULL(""ProfitCode"", '') = ?
                       AND IFNULL(""OcrCode2"", '') = ?
                       AND IFNULL(""OcrCode3"", '') = ?";

                foreach (var item in request.Items)
                {
                    var itemResult = new GlLineAttributionItemResult
                    {
                        ErpEntryId = item.ErpEntryId,
                        ErpLineId = item.ErpLineId,
                    };

                    using var command = new OdbcCommand(updateSql, connection, transaction);
                    command.CommandTimeout = WriteCommandTimeoutSeconds;
                    // L'ordine dei parametri segue esattamente i '?' del testo: SET, poi PK, poi atteso.
                    command.Parameters.AddWithValue("@Project", Trunc(item.Desired.ProjectCode, 20));
                    command.Parameters.AddWithValue("@Dim1", Trunc(item.Desired.Dimension1Code, 8));
                    command.Parameters.AddWithValue("@Dim2", Trunc(item.Desired.Dimension2Code, 8));
                    command.Parameters.AddWithValue("@Dim3", Trunc(item.Desired.Dimension3Code, 8));
                    command.Parameters.AddWithValue("@TransId", item.ErpEntryId);
                    command.Parameters.AddWithValue("@LineId", item.ErpLineId);
                    command.Parameters.AddWithValue("@ExpProject", item.Expected.ProjectCode ?? string.Empty);
                    command.Parameters.AddWithValue("@ExpDim1", item.Expected.Dimension1Code ?? string.Empty);
                    command.Parameters.AddWithValue("@ExpDim2", item.Expected.Dimension2Code ?? string.Empty);
                    command.Parameters.AddWithValue("@ExpDim3", item.Expected.Dimension3Code ?? string.Empty);

                    var affected = await command.ExecuteNonQueryAsync();

                    if (affected > 1)
                    {
                        // La WHERE è sulla PK: non dovrebbe mai poter accadere. Se accade, qualcosa non
                        // torna nello schema e fermarsi è l'unica reazione sana.
                        transaction.Rollback();
                        result.ErrorMessage = $"UPDATE su PK ({item.ErpEntryId},{item.ErpLineId}) ha toccato {affected} righe. Rollback.";
                        _logger.LogError("UPDATE JDT1 su PK ha toccato {Affected} righe: schema inatteso. Batch annullato.", affected);
                        return result;
                    }

                    if (affected == 0)
                    {
                        // Riga inesistente, oppure cambiata sotto (compare-and-swap fallito).
                        var exists = await LineExistsAsync(connection, transaction, item.ErpEntryId, item.ErpLineId);
                        itemResult.NotFound = !exists;
                        itemResult.Conflict = exists;
                        if (exists) result.Conflicts++;
                    }
                    else
                    {
                        itemResult.Updated = true;
                        result.Updated++;
                    }

                    itemResult.Current = await ReadCurrentAsync(connection, transaction, item.ErpEntryId, item.ErpLineId);
                    result.Items.Add(itemResult);

                    _logger.LogInformation(
                        "GL attribution {Outcome}: TransId={TransId} Line={LineId} attore={Actor} " +
                        "prima=[prj={PrevPrj} d1={PrevD1} d2={PrevD2} d3={PrevD3}] " +
                        "dopo=[prj={NewPrj} d1={NewD1} d2={NewD2} d3={NewD3}]",
                        itemResult.Updated ? "OK" : (itemResult.Conflict ? "CONFLITTO" : "NON TROVATA"),
                        item.ErpEntryId, item.ErpLineId, CurrentActor(),
                        item.Expected.ProjectCode, item.Expected.Dimension1Code, item.Expected.Dimension2Code, item.Expected.Dimension3Code,
                        item.Desired.ProjectCode, item.Desired.Dimension1Code, item.Desired.Dimension2Code, item.Desired.Dimension3Code);
                }

                transaction.Commit();
                result.Success = true;
                return result;
            }
            catch (Exception ex)
            {
                try { transaction.Rollback(); }
                catch (Exception rollbackEx) { _logger.LogError(rollbackEx, "Rollback fallito dopo errore su UPDATE GL."); }
                _logger.LogError(ex, "UPDATE GL fallito, batch annullato ({Count} righe).", request.Items.Count);
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helper privati
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Codici progetto/centro di costo che non esistono in anagrafica. Nel DB non c'è alcuna FK:
        /// senza questo controllo un typo arriverebbe intatto fino a SAP (ci sono già valori orfani).
        /// La dimensione è parte della chiave: un codice valido per la dim.2 non lo è per la dim.3.
        /// </summary>
        private async Task<List<string>> FindInvalidCodesAsync(OdbcConnection connection, List<GlLineAttributionItem> items)
        {
            var invalid = new List<string>();

            var projectCodes = items
                .Select(i => i.Desired.ProjectCode)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (projectCodes.Count > 0)
            {
                var known = await ReadCodeSetAsync(connection,
                    $@"SELECT ""PrjCode"" FROM ""{_schema}"".""OPRJ"" WHERE ""PrjCode"" IN ({Placeholders(projectCodes.Count)})",
                    projectCodes);
                invalid.AddRange(projectCodes.Where(c => !known.Contains(c)).Select(c => $"progetto '{c}'"));
            }

            foreach (var (dim, codes) in new[]
                     {
                         (1, items.Select(i => i.Desired.Dimension1Code)),
                         (2, items.Select(i => i.Desired.Dimension2Code)),
                         (3, items.Select(i => i.Desired.Dimension3Code)),
                     })
            {
                var list = codes.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (list.Count == 0) continue;

                var known = await ReadCodeSetAsync(connection,
                    $@"SELECT ""OcrCode"" FROM ""{_schema}"".""OOCR"" WHERE ""DimCode"" = {dim} AND ""Active"" = 'Y' AND ""OcrCode"" IN ({Placeholders(list.Count)})",
                    list);
                invalid.AddRange(list.Where(c => !known.Contains(c)).Select(c => $"dimensione {dim} '{c}'"));
            }

            return invalid;
        }

        private async Task<HashSet<string>> ReadCodeSetAsync(OdbcConnection connection, string sql, List<string> parameters)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var command = new OdbcCommand(sql, connection);
            command.CommandTimeout = WriteCommandTimeoutSeconds;
            foreach (var p in parameters) command.Parameters.AddWithValue("@Code", p);
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                if (!reader.IsDBNull(0)) set.Add(reader.GetString(0));
            return set;
        }

        private async Task<bool> LineExistsAsync(OdbcConnection connection, OdbcTransaction transaction, int transId, int lineId)
        {
            var sql = $@"SELECT COUNT(*) FROM ""{_schema}"".""JDT1"" WHERE ""TransId"" = ? AND ""Line_ID"" = ?";
            using var command = new OdbcCommand(sql, connection, transaction);
            command.CommandTimeout = WriteCommandTimeoutSeconds;
            command.Parameters.AddWithValue("@TransId", transId);
            command.Parameters.AddWithValue("@LineId", lineId);
            var count = await command.ExecuteScalarAsync();
            return count != null && Convert.ToInt32(count) > 0;
        }

        private async Task<GlLineAttributionValues?> ReadCurrentAsync(OdbcConnection connection, OdbcTransaction transaction, int transId, int lineId)
        {
            var sql = $@"
                SELECT IFNULL(""Project"",''), IFNULL(""ProfitCode"",''), IFNULL(""OcrCode2"",''), IFNULL(""OcrCode3"",'')
                FROM ""{_schema}"".""JDT1"" WHERE ""TransId"" = ? AND ""Line_ID"" = ?";
            using var command = new OdbcCommand(sql, connection, transaction);
            command.CommandTimeout = WriteCommandTimeoutSeconds;
            command.Parameters.AddWithValue("@TransId", transId);
            command.Parameters.AddWithValue("@LineId", lineId);
            using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;
            return new GlLineAttributionValues
            {
                ProjectCode = reader.GetString(0),
                Dimension1Code = reader.GetString(1),
                Dimension2Code = reader.GetString(2),
                Dimension3Code = reader.GetString(3),
            };
        }

        private static string Placeholders(int count) => string.Join(",", Enumerable.Repeat("?", count));

        /// <summary>Nessun vincolo di lunghezza nel DB: troncare qui evita un errore a runtime.</summary>
        private static string Trunc(string? value, int maxLength)
        {
            var v = value ?? string.Empty;
            return v.Length <= maxLength ? v : v.Substring(0, maxLength);
        }

        private static string? NullIfEmpty(System.Data.Common.DbDataReader reader, int ordinal)
        {
            if (reader.IsDBNull(ordinal)) return null;
            var v = reader.GetString(ordinal);
            return string.IsNullOrWhiteSpace(v) ? null : v;
        }

        private string CurrentActor()
        {
            var user = _httpContextAccessor.HttpContext?.User;
            return user?.Identity?.Name ?? "sconosciuto";
        }

        internal static string? MapStatementSection(int groupMask) => groupMask switch
        {
            4 => GlStatementSection.ProductionValue,
            5 => GlStatementSection.ProductionCost,
            6 => GlStatementSection.OperatingCost,
            7 => GlStatementSection.OtherIncomeExpense,
            8 => GlStatementSection.Extraordinary,
            _ => null,
        };

        // Mappatura verificata empiricamente su questo impianto incrociando TransId con i documenti:
        // 13→OINV, 18→OPCH, 14→ORIN, 19→ORPC hanno tutti corrisposto al 100%.
        internal static string MapSourceDocType(string? transType) => transType switch
        {
            "13" => GlSourceDocType.SalesInvoice,
            "18" => GlSourceDocType.PurchaseInvoice,
            "14" => GlSourceDocType.SalesCreditNote,
            "19" => GlSourceDocType.PurchaseCreditNote,
            "30" => GlSourceDocType.ManualJournal,
            "24" or "46" => GlSourceDocType.Payment,
            _ => GlSourceDocType.Other,
        };

        internal static string? MapCounterpartyType(string? transType, string? cardCode)
        {
            if (string.IsNullOrWhiteSpace(cardCode)) return null;
            return transType switch
            {
                "13" or "14" => "customer",
                "18" or "19" => "supplier",
                _ => null,
            };
        }
    }
}
