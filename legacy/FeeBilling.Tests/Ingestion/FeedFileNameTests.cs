using System;
using FeeBilling.Ingestion.Wcf;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FeeBilling.Tests.Ingestion
{
    [TestClass]
    public class FeedFileNameTests
    {
        [TestMethod]
        public void Parse_StandardName()
        {
            var name = FeedFileName.Parse("NBIN_20160930_POS.txt");

            Assert.AreEqual("NBIN", name.CustodianCode);
            Assert.AreEqual(new DateTime(2016, 9, 30), name.FileDate);
            Assert.AreEqual("POS", name.FileType);
        }

        [TestMethod]
        public void Parse_LowercaseCustodian_IsUppercased()
        {
            Assert.AreEqual("FIDC", FeedFileName.Parse("fidc_20160930_pos.txt").CustodianCode);
        }

        [TestMethod]
        public void TryParse_Garbage_ReturnsFalse()
        {
            FeedFileName result;
            Assert.IsFalse(FeedFileName.TryParse("positions.txt", out result));
        }

        // FB-251: Pershing's agent sends ISO dates in the file name.
        [TestMethod]
        public void Parse_IsoDateInName()
        {
            Assert.AreEqual(new DateTime(2016, 9, 30), FeedFileName.Parse("PERS_2016-09-30_POS.txt").FileDate);
        }
    }
}
