using FeeBilling.Data;
using FeeBilling.Web.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FeeBilling.Tests.Web
{
    [TestClass]
    public class FeeScheduleDtoTests
    {
        private static FeeSchedule StandardTiered()
        {
            var schedule = new FeeSchedule
            {
                Id = 1,
                Code = "STD-TIERED",
                Name = "Standard tiered",
                ScheduleType = "TIERED",
                MinimumAnnualFee = 1000m,
                Currency = "CAD",
                IsActive = true
            };
            schedule.Tiers.Add(new FeeTier { LowerBound = 5000000m, UpperBound = null, AnnualRate = 0.005m });
            schedule.Tiers.Add(new FeeTier { LowerBound = 0m, UpperBound = 1000000m, AnnualRate = 0.01m });
            schedule.Tiers.Add(new FeeTier { LowerBound = 1000000m, UpperBound = 5000000m, AnnualRate = 0.0075m });
            return schedule;
        }

        [TestMethod]
        public void From_MapsScalarFields()
        {
            var dto = FeeScheduleDto.From(StandardTiered());

            Assert.AreEqual("STD-TIERED", dto.Code);
            Assert.AreEqual("TIERED", dto.ScheduleType);
            Assert.AreEqual(1000m, dto.MinimumAnnualFee);
        }

        [TestMethod]
        public void From_OrdersTiersByLowerBound()
        {
            var dto = FeeScheduleDto.From(StandardTiered());

            Assert.AreEqual(0m, dto.Tiers[0].LowerBound);
            Assert.AreEqual(0.0075m, dto.Tiers[1].AnnualRate);
            Assert.IsNull(dto.Tiers[2].UpperBound);
        }
    }
}
