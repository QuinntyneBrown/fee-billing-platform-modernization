using System;
using FeeBilling.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FeeBilling.Tests.Core
{
    // These need the FeeBilling database AND an HttpContext (FeeCalculator gets its DbContext
    // from HttpContext.Current.Items). The first three were un-ignored in 2019 to "get them
    // running again". They have been red since.
    [TestClass]
    public class FeeCalculatorTests
    {
        private static readonly DateTime PeriodEnd = new DateTime(2016, 9, 30);

        [TestMethod]
        public void CalculateQuarterlyFee_MidTier()
        {
            Assert.AreEqual(5312.50m, FeeCalculator.CalculateQuarterlyFee(1001, PeriodEnd));
        }

        [TestMethod]
        public void CalculateQuarterlyFee_OnTierBoundary()
        {
            Assert.AreEqual(2500.00m, FeeCalculator.CalculateQuarterlyFee(1002, PeriodEnd));
        }

        [TestMethod]
        public void CalculateQuarterlyFee_BelowMinimum_ChargesMinimum()
        {
            Assert.AreEqual(250.00m, FeeCalculator.CalculateQuarterlyFee(1003, PeriodEnd));
        }

        [TestMethod, Ignore("Needs FeeBilling database")]
        public void CalculateQuarterlyFee_TopTier()
        {
            Assert.AreEqual(15625.00m, FeeCalculator.CalculateQuarterlyFee(1020, PeriodEnd));
        }

        [TestMethod, Ignore("Needs FeeBilling database")]
        public void CalculateQuarterlyFee_ZeroAum_ChargesMinimum()
        {
            Assert.AreEqual(250.00m, FeeCalculator.CalculateQuarterlyFee(1021, PeriodEnd));
        }

        [TestMethod, Ignore("Needs FeeBilling database")]
        public void CalculateQuarterlyFee_CashSleeveExcluded()
        {
            Assert.AreEqual(2500.00m, FeeCalculator.CalculateQuarterlyFee(1006, PeriodEnd));
        }

        [TestMethod, Ignore("Needs FeeBilling database")]
        public void CalculateQuarterlyFee_UsesCache()
        {
            var first = FeeCalculator.CalculateQuarterlyFee(1001, PeriodEnd);
            var second = FeeCalculator.CalculateQuarterlyFee(1001, PeriodEnd);
            Assert.AreEqual(first, second);
        }

        [TestMethod, Ignore("Needs FeeBilling database")]
        public void CalculateQuarterlyFee_UnknownAccount_Throws()
        {
            FeeCalculator.CalculateQuarterlyFee(-1, PeriodEnd);
        }

        [TestMethod, Ignore("Flaky - cache from previous test")]
        public void CalculateQuarterlyFee_AumChangedAfterCache_UsesNewAum()
        {
            Assert.Inconclusive();
        }

        [TestMethod, Ignore("Needs FeeBilling database")]
        public void CalculateQuarterlyFee_ExcludedSecurity_NotBilled()
        {
            Assert.Inconclusive();
        }
    }
}
