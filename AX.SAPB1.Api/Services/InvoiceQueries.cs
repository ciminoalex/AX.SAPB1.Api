namespace AX.SAPB1.Api.Services
{
    /// <summary>
    /// Le due SELECT del feed fatture (<c>/api/invoices</c>): testate e righe di un blocco di documenti A/R
    /// (OINV/INV1 oppure ORIN/RIN1).
    ///
    /// <para><b>Perché i campi AX.360 sono opzionali.</b> <c>U_AX360_InvId</c>, <c>U_AX360_DocType</c> e
    /// <c>U_AX360_RevCat</c> li crea il bootstrap degli UDF, ma una company servita in sola lettura (HTDI,
    /// <c>Companies:htdi</c>) non li riceve mai: il bootstrap è spento e il servizio non scrive metadati nel
    /// suo SAP. Una colonna assente fa fallire l'INTERA SELECT, quindi il feed fatture di quella company
    /// non funzionava affatto. Ogni campo si seleziona solo se la tabella lo ha; altrimenti la colonna
    /// esce NULL, con la stessa posizione, e il resto del documento arriva intero.</para>
    ///
    /// <para>Costruttori puri, senza connessione: i test verificano le query senza un database.</para>
    /// </summary>
    public static class InvoiceQueries
    {
        /// <summary>Presenza dei campi AX.360 sulle tabelle lette dal feed fatture.</summary>
        public sealed record Ax360Fields(
            bool HeaderInvId, bool HeaderDocType, bool LineRevCat, bool ItemRevCat, bool AccountRevCat)
        {
            /// <summary>Tutti presenti: la company di MTF, dove il bootstrap li ha creati.</summary>
            public static Ax360Fields All => new(true, true, true, true, true);
        }

        private const string NullText = "CAST(NULL AS NVARCHAR(254))";

        /// <summary>
        /// Testate. Le colonne 10 e 11 sono sempre <c>U_AX360_InvId</c> e <c>U_AX360_DocType</c> (o NULL) per
        /// le fatture; le note di credito non le selezionano (non nascono mai dal portale).
        /// </summary>
        public static string BuildHeaderQuery(string schema, string headerTable, bool isCreditNote, bool hasSince, Ax360Fields fields)
        {
            var udfSelect = string.Empty;
            if (!isCreditNote)
            {
                var invId = fields.HeaderInvId ? $@"T.""{Ax360Udf.Col(Ax360Udf.InvId)}""" : NullText;
                var docType = fields.HeaderDocType ? $@"T.""{Ax360Udf.Col(Ax360Udf.DocType)}""" : NullText;
                udfSelect = $", {invId}, {docType}";
            }
            var sinceClause = hasSince ? @"WHERE T.""UpdateDate"" >= ?" : string.Empty;

            return $@"
                SELECT
                    T.""DocEntry"", T.""DocNum"", T.""CardCode"",
                    T.""DocDate"", T.""DocDueDate"", T.""DocCur"",
                    T.""DocTotal"", T.""VatSum"", T.""PaidToDate"", T.""Comments""{udfSelect}
                FROM ""{schema}"".""{headerTable}"" T
                {sinceClause}
                ORDER BY T.""DocDate"", T.""DocEntry""";
        }

        /// <summary>
        /// Righe, con la categoria di ricavo: precedenza alla riga documento, poi all'articolo (OITM) per le
        /// righe a articolo e al conto (OACT) per quelle di servizio. Ogni fonte assente vale NULL.
        /// </summary>
        public static string BuildLineQuery(string schema, string headerTable, string lineTable, bool hasSince, Ax360Fields fields)
        {
            var udf = Ax360Udf.Col(Ax360Udf.RevCat);
            var line = fields.LineRevCat ? $@"NULLIF(L.""{udf}"", '')" : NullText;
            var item = fields.ItemRevCat ? $@"ITM.""{udf}""" : NullText;
            var account = fields.AccountRevCat ? $@"ACT.""{udf}""" : NullText;

            return $@"
                SELECT
                    L.""DocEntry"", L.""LineNum"", L.""ItemCode"", L.""Dscription"",
                    L.""Quantity"", L.""Price"", L.""LineTotal"", L.""VatPrcnt"", L.""GTotal"",
                    COALESCE(
                        {line},
                        CASE WHEN L.""ItemCode"" IS NOT NULL AND L.""ItemCode"" <> ''
                             THEN {item}
                             ELSE {account} END) AS ""RevenueCategory""
                FROM ""{schema}"".""{lineTable}"" L
                INNER JOIN ""{schema}"".""{headerTable}"" H ON H.""DocEntry"" = L.""DocEntry""
                LEFT JOIN ""{schema}"".""OITM"" ITM ON ITM.""ItemCode"" = L.""ItemCode""
                LEFT JOIN ""{schema}"".""OACT"" ACT ON ACT.""AcctCode"" = L.""AcctCode""
                {(hasSince ? @"WHERE H.""UpdateDate"" >= ?" : string.Empty)}
                ORDER BY L.""DocEntry"", L.""LineNum""";
        }
    }
}
