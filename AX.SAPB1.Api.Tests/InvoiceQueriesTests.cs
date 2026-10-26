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
            Assert.Contains(@"WHERE T.""UpdateDate"" >= ?", header);
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
