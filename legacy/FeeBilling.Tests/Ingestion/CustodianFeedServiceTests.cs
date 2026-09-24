using System.Globalization;
using System.Text;
using System.Threading;
using FeeBilling.Ingestion.Wcf;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FeeBilling.Tests.Ingestion
{
    [TestClass]
    public class CustodianFeedServiceTests
    {
        private const string Header = "ACCOUNT     NAME                        MARKET VALUE      AS OF     ";

        private static byte[] File(params string[] lines)
        {
            return Encoding.Default.GetBytes(Header + "\n" + string.Join("\n", lines));
        }

        private static string Line(string account, string name, string marketValue, string asOf)
        {
            return account.PadRight(12) + name.PadRight(28) + marketValue.PadLeft(18) + asOf;
        }

        // FB-274: the Montreal data centre servers run fr-CA.
        [TestMethod]
        public void SubmitPositionFile_OnFrenchCanadianServer_ParsesAmounts()
        {
            var original = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("fr-CA");
                var service = new CustodianFeedService();

                var receipt = service.SubmitPositionFile("NBIN", "NBIN_20160930_POS.txt", File(
                    Line("LFG000002001", "Tremblay, Marie - REER", "1234567.89", "2016-09-30"),
                    Line("LFG000002002", "Gagnon, Jean - CELI", "98765.43", "2016-09-30")));

                Assert.AreEqual(2, receipt.Accepted);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
            }
        }

        [TestMethod, Ignore("Needs FeeBilling database")]
        public void SubmitPositionFile_StagesBatch()
        {
            Assert.Inconclusive();
        }

        [TestMethod, Ignore("Needs FeeBilling database")]
        public void ProcessPendingBatches_UpsertsPositions()
        {
            Assert.Inconclusive();
        }
    }
}
