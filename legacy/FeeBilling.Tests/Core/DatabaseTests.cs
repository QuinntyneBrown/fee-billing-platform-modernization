using System;
using System.Collections.Generic;
using FeeBilling.Core;
using FeeBilling.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FeeBilling.Tests.Core
{
    [TestClass]
    public class BlendedFeeCalculatorTests
    {
        private static readonly DateTime PeriodEnd = new DateTime(2016, 9, 30);

        [TestMethod, Ignore("Needs FeeBilling database")]
        public void CalculateQuarterlyFee_Blended_ChargesWholeAumAtHighestRate()
        {
            Assert.AreEqual(4687.50m, BlendedFeeCalculator.CalculateQuarterlyFee(1004, PeriodEnd));
        }

        [TestMethod, Ignore("Needs FeeBilling database")]
        public void CalculateQuarterlyFee_Blended_BelowFirstBoundary()
        {
            Assert.Inconclusive();
        }

        [TestMethod, Ignore("Needs FeeBilling database")]
        public void CalculateQuarterlyFee_Blended_BelowMinimum()
        {
            Assert.Inconclusive();
        }

        [TestMethod, Ignore("Needs FeeBilling database")]
        public void CalculateQuarterlyFee_Blended_TopTier()
        {
            Assert.Inconclusive();
        }
    }

    [TestClass]
    public class HouseholdFeeServiceTests
    {
        private static readonly DateTime PeriodEnd = new DateTime(2016, 9, 30);

        [TestMethod, Ignore("Needs FeeBilling database")]
        public void CalculateHouseholdQuarterlyFee_CombinesMemberAum()
        {
            IList<FeeBilling.Domain.ValueObjects.AccountAum> members;
            Assert.AreEqual(6250.00m, HouseholdFeeService.CalculateHouseholdQuarterlyFee(100, PeriodEnd, out members));
        }

        [TestMethod, Ignore("Needs FeeBilling database")]
        public void CalculateHouseholdQuarterlyFee_BelowMinimum()
        {
            Assert.Inconclusive();
        }

        [TestMethod, Ignore("Needs FeeBilling database")]
        public void CalculateAndAllocate_AllocatesToEveryMember()
        {
            Assert.AreEqual(3, HouseholdFeeService.CalculateAndAllocate(100, PeriodEnd).Count);
        }
    }

    [TestClass]
    public class AumServiceTests
    {
        [TestMethod, Ignore("Needs FeeBilling database")]
        public void GetBillableAum_ExcludesCashSleeve()
        {
            Assert.AreEqual(1000000m, AumService.GetBillableAum(1006, new DateTime(2016, 9, 30)));
        }
    }

    [TestClass]
    public class BillingRunServiceTests
    {
        [TestMethod]
        public void IsHouseholdBilled_HouseholdWithoutOwnSchedule_True()
        {
            Assert.IsTrue(BillingRunService.IsHouseholdBilled(new Account { HouseholdId = 100, FeeScheduleId = null }));
        }

        [TestMethod]
        public void IsHouseholdBilled_OwnSchedule_False()
        {
            Assert.IsFalse(BillingRunService.IsHouseholdBilled(new Account { HouseholdId = 100, FeeScheduleId = 1 }));
        }

        [TestMethod]
        public void IsHouseholdBilled_NoHousehold_False()
        {
            Assert.IsFalse(BillingRunService.IsHouseholdBilled(new Account { HouseholdId = null, FeeScheduleId = 1 }));
        }

        [TestMethod, Ignore("Needs FeeBilling database")]
        public void Enqueue_ReturnsNewRunId()
        {
            Assert.IsTrue(BillingRunService.Enqueue(1, new DateTime(2016, 9, 30), "test") > 0);
        }
    }

    [TestClass]
    public class DbContextFactoryTests
    {
        // BillingRunner needs this to work without IIS (FB-402).
        [TestMethod]
        public void Current_OutsideAspNet_ReturnsContext()
        {
            Assert.IsNotNull(DbContextFactory.Current);
        }
    }
}
