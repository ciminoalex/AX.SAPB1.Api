using AX.SAPB1.Api.Services;
using Xunit;

namespace AX.SAPB1.Api.Tests
{
    /// <summary>
    /// Feed fatture su una company senza i campi AX.360 (HTDI, in sola lettura): le query non devono nominarli,
    /// altrimenti la SELECT fallisce intera. Con i campi presenti (MTF) le query restano quelle di sempre.
    /// </summary>
    public class InvoiceQueriesTests
    {
        private static readonly InvoiceQueries.Ax360Fields None = new(false, false, false, false, false);

        [Fact]
        public void SenzaCampiAx360_LeQueryNonLiNominano()
        {
            var header = InvoiceQueries.BuildHeaderQuery("SBOHITECH_PROD", "OINV", isCreditNote: false, hasSince: true, None);
            var lines = InvoiceQueries.BuildLineQuery("SBOHITECH_PROD", "OINV", "INV1", hasSince: true, None);

            Assert.DoesNotContain("U_AX360", header);
            Assert.DoesNotContain("U_AX360", lines);
            // La posizione delle colonne resta quella che il lettore si aspetta: 12 colonne di testata.
            Assert.Equal(2, CountOccurrences(header, "CAST(NULL AS NVARCHAR(254))"));
            Assert.Contains(@"AS ""RevenueCategory""", lines);
        }

        [Fact]
        public void ConTuttiICampi_LeQueryLiLeggono()
        {
            var header = InvoiceQueries.BuildHeaderQuery("SBO_MTF", "OINV", isCreditNote: false, hasSince: false, InvoiceQueries.Ax360Fields.All);
            var lines = InvoiceQueries.BuildLineQuery("SBO_MTF", "OINV", "INV1", hasSince: false, InvoiceQueries.Ax360Fields.All);

            Assert.Contains(@"T.""U_AX360_InvId""", header);
            Assert.Contains(@"T.""U_AX360_DocType""", header);
            Assert.Contains(@"NULLIF(L.""U_AX360_RevCat"", '')", lines);
            Assert.Contains(@"ITM.""U_AX360_RevCat""", lines);
            Assert.Contains(@"ACT.""U_AX360_RevCat""", lines);
            Assert.DoesNotContain("CAST(NULL", header);
            Assert.DoesNotContain("WHERE", header);
        }

        [Fact]
        public void NoteDiCredito_NonSelezionanoICampiDiCorrelazione()
        {
            var header = InvoiceQueries.BuildHeaderQuery("SBO_MTF", "ORIN", isCreditNote: true, hasSince: true, InvoiceQueries.Ax360Fields.All);

            Assert.DoesNotContain("U_AX360", header);
            Assert.Contains(@"WHERE (T.""UpdateDate"" >= ?", header);
            // Le note di credito si cercano in ITR1 con il loro tipo oggetto, 14.
            Assert.Contains(@"RL.""SrcObjTyp"" = '14'", header);
        }

        [Fact]
        public void ConWatermark_EntranoAncheIDocumentiRiconciliati()
        {
            // Un incasso non avanza UpdateDate della fattura che salda: la fattura rientra nel delta solo
            // perché una riconciliazione registrata dopo il watermark la nomina.
            var header = InvoiceQueries.BuildHeaderQuery("SBO_MTF", "OINV", isCreditNote: false, hasSince: true, InvoiceQueries.Ax360Fields.All);
            var lines = InvoiceQueries.BuildLineQuery("SBO_MTF", "OINV", "INV1", hasSince: true, InvoiceQueries.Ax360Fields.All);

            Assert.Contains(@"T.""DocEntry"" IN (", header);
            Assert.Contains(@"H.""DocEntry"" IN (", lines);
            foreach (var query in new[] { header, lines })
            {
                Assert.Contains(@"""SBO_MTF"".""ITR1"" RL", query);
                Assert.Contains(@"""SBO_MTF"".""OITR"" RH", query);
                // La data di registrazione, non la data valuta (ReconDate), che può precederla di giorni.
                Assert.Contains(@"RH.""CreateDate"" >= ?", query);
                Assert.DoesNotContain("ReconDate", query);
                Assert.Contains(@"RL.""SrcObjTyp"" = '13'", query);
                Assert.Equal(InvoiceQueries.SinceParameterCount, CountOccurrences(query, "?"));
            }
        }

        [Fact]
        public void SenzaWatermark_NessunFiltro()
        {
            var lines = InvoiceQueries.BuildLineQuery("SBO_MTF", "OINV", "INV1", hasSince: false, InvoiceQueries.Ax360Fields.All);

            Assert.DoesNotContain("ITR1", lines);
            Assert.DoesNotContain("?", lines);
        }

        [Fact]
        public void TestataSconosciuta_SiRifiuta()
        {
            Assert.Throws<ArgumentException>(() => InvoiceQueries.SinceFilter("SBO_MTF", "OPCH", "H"));
        }

        [Fact]
        public void CategoriaSoloSullArticolo_LeAltreFontiSonoNull()
        {
            var fields = new InvoiceQueries.Ax360Fields(true, true, LineRevCat: false, ItemRevCat: true, AccountRevCat: false);
            var lines = InvoiceQueries.BuildLineQuery("X", "OINV", "INV1", hasSince: false, fields);

            Assert.Contains(@"ITM.""U_AX360_RevCat""", lines);
            Assert.DoesNotContain(@"L.""U_AX360_RevCat""", lines);
            Assert.DoesNotContain(@"ACT.""U_AX360_RevCat""", lines);
        }

        private static int CountOccurrences(string text, string value)
        {
            var count = 0;
            for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal))
                count++;
            return count;
        }
    }
}
