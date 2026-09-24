using System.Collections.Generic;
using System.Linq;
using FeeBilling.Core;
using FeeBilling.Domain.ValueObjects;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FeeBilling.Tests.Core
{
    [TestClass]
    public class HouseholdAllocatorTests
    {
        [TestMethod]
        public void Allocate_TwoEqualMembers_SplitsEvenly()
        {
            var result = HouseholdAllocator.Allocate(100.00m, new List<AccountAum>
            {
                new AccountAum(1, 500000m),
                new AccountAum(2, 500000m)
            });

            Assert.AreEqual(50.00m, result[1]);
            Assert.AreEqual(50.00m, result[2]);
        }

        [TestMethod]
        public void Allocate_SingleMember_GetsWholeFee()
        {
            var result = HouseholdAllocator.Allocate(625.00m, new List<AccountAum> { new AccountAum(7, 1250000m) });

            Assert.AreEqual(625.00m, result[7]);
        }

        [TestMethod]
        public void Allocate_ReturnsOneEntryPerMember()
        {
            var result = HouseholdAllocator.Allocate(300m, new List<AccountAum>
            {
                new AccountAum(1, 1m), new AccountAum(2, 1m), new AccountAum(3, 1m)
            });

            Assert.AreEqual(3, result.Count);
        }

        [TestMethod]
        public void Allocate_ProportionalToAum()
        {
            var result = HouseholdAllocator.Allocate(400.00m, new List<AccountAum>
            {
                new AccountAum(1, 750000m),
                new AccountAum(2, 250000m)
            });

            Assert.AreEqual(300.00m, result[1]);
            Assert.AreEqual(100.00m, result[2]);
        }

        [TestMethod]
        public void Allocate_MidpointRoundsAwayFromZero()
        {
            // 6,301.37 x 50% = 3,150.685 -> 3,150.69 (not banker's 3,150.68)
            var result = HouseholdAllocator.Allocate(6301.37m, new List<AccountAum>
            {
                new AccountAum(1, 1500000m),
                new AccountAum(2, 1500000m)
            });

            Assert.AreEqual(3150.69m, result[1]);
        }

        [TestMethod]
        public void Allocate_NoMembers_ReturnsEmpty()
        {
            var result = HouseholdAllocator.Allocate(100m, new List<AccountAum>());

            Assert.AreEqual(0, result.Count);
        }

        // FB-233: household statements don't add up. Reproduced here; fix not scheduled.
        [TestMethod]
        public void Allocate_ThreeMembers_SumEqualsHouseholdFee()
        {
            var result = HouseholdAllocator.Allocate(6301.37m, new List<AccountAum>
            {
                new AccountAum(1, 1500000m),
                new AccountAum(2, 1000000m),
                new AccountAum(3, 500000m)
            });

            Assert.AreEqual(6301.37m, result.Values.Sum());
        }

        [TestMethod]
        public void Allocate_ZeroAumHousehold_AllocatesZero()
        {
            var result = HouseholdAllocator.Allocate(625.00m, new List<AccountAum>
            {
                new AccountAum(1, 0m),
                new AccountAum(2, 0m)
            });

            Assert.AreEqual(0m, result[1]);
            Assert.AreEqual(0m, result[2]);
        }
    }
}
