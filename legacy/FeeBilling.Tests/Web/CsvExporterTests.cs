using System.Data;
using System.Globalization;
using System.Threading;
using FeeBilling.Web.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FeeBilling.Tests.Web
{
    [TestClass]
    public class CsvExporterTests
    {
        private CultureInfo _originalCulture;

        [TestInitialize]
        public void SaveCulture()
        {
            _originalCulture = Thread.CurrentThread.CurrentCulture;
        }

        [TestCleanup]
        public void RestoreCulture()
        {
            Thread.CurrentThread.CurrentCulture = _originalCulture;
        }

        [TestMethod]
        public void FormatValue_Null_ReturnsEmpty()
        {
            Assert.AreEqual(string.Empty, CsvExporter.FormatValue(null));
        }

        [TestMethod]
        public void FormatValue_TextWithComma_IsQuoted()
        {
            Assert.AreEqual("\"Carter, Janet - RRSP\"", CsvExporter.FormatValue("Carter, Janet - RRSP"));
        }

        [TestMethod]
        public void FormatValue_TextWithQuote_IsEscaped()
        {
            Assert.AreEqual("\"The \"\"Big\"\" Account\"", CsvExporter.FormatValue("The \"Big\" Account"));
        }

        [TestMethod]
        public void ToCsv_WritesHeaderThenRows()
        {
            var table = new DataTable();
            table.Columns.Add("AccountNumber");
            table.Columns.Add("AccountName");
            table.Rows.Add("MRW000001001", "Carter");

            var csv = CsvExporter.ToCsv(table);

            StringAssert.StartsWith(csv, "AccountNumber,AccountName");
            StringAssert.Contains(csv, "MRW000001001,Carter");
        }

        // FB-261: Laurentien's export opened in Excel with every amount in one column.
        [TestMethod]
        public void FormatValue_Decimal_UsesPeriodOnFrenchCanadianServer()
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("fr-CA");

            Assert.AreEqual("5312.50", CsvExporter.FormatValue(5312.50m));
        }
    }
}
