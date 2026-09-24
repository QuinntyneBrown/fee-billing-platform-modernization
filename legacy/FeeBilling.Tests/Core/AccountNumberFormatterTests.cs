using System;
using FeeBilling.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FeeBilling.Tests.Core
{
    [TestClass]
    public class AccountNumberFormatterTests
    {
        [TestMethod]
        public void Mask_TwelveCharacters_ShowsLastFour()
        {
            Assert.AreEqual("********1001", AccountNumberFormatter.Mask("MRW000001001"));
        }

        [TestMethod]
        public void Mask_FourOrFewer_ReturnsUnchanged()
        {
            Assert.AreEqual("1001", AccountNumberFormatter.Mask("1001"));
        }

        [TestMethod, ExpectedException(typeof(ArgumentNullException))]
        public void PadForFeed_Null_Throws()
        {
            AccountNumberFormatter.PadForFeed(null);
        }
    }
}
