using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using FeeBilling.Data;

namespace FeeBilling.Web.Models
{
    public class FeeScheduleDto
    {
        public int Id { get; set; }

        [Required, StringLength(20)]
        public string Code { get; set; }

        [Required, StringLength(100)]
        public string Name { get; set; }

        [Required, RegularExpression("TIERED|BLENDED|FLAT")]
        public string ScheduleType { get; set; }

        public bool IsHousehold { get; set; }

        [Range(0, 1000000)]
        public decimal MinimumAnnualFee { get; set; }

        public decimal? FlatAnnualFee { get; set; }

        public string Currency { get; set; }

        public bool IsActive { get; set; }

        public List<FeeTierDto> Tiers { get; set; }

        public static FeeScheduleDto From(FeeSchedule s)
        {
            return new FeeScheduleDto
            {
                Id = s.Id,
                Code = s.Code,
                Name = s.Name,
                ScheduleType = s.ScheduleType,
                IsHousehold = s.IsHousehold,
                MinimumAnnualFee = s.MinimumAnnualFee,
                FlatAnnualFee = s.FlatAnnualFee,
                Currency = s.Currency,
                IsActive = s.IsActive,
                Tiers = s.Tiers
                    .OrderBy(t => t.LowerBound)
                    .Select(t => new FeeTierDto { LowerBound = t.LowerBound, UpperBound = t.UpperBound, AnnualRate = t.AnnualRate })
                    .ToList()
            };
        }

        public void ApplyTo(FeeSchedule schedule, FeeBillingEntities db)
        {
            schedule.Code = Code;
            schedule.Name = Name;
            schedule.ScheduleType = ScheduleType;
            schedule.IsHousehold = IsHousehold;
            schedule.MinimumAnnualFee = MinimumAnnualFee;
            schedule.FlatAnnualFee = FlatAnnualFee;
            schedule.Currency = string.IsNullOrEmpty(Currency) ? "CAD" : Currency;

            // Replace all tiers. (No validation that the bands are contiguous or non-overlapping.)
            db.FeeTiers.RemoveRange(schedule.Tiers.ToList());
            foreach (var tier in Tiers ?? new List<FeeTierDto>())
            {
                schedule.Tiers.Add(new FeeTier
                {
                    LowerBound = tier.LowerBound,
                    UpperBound = tier.UpperBound,
                    AnnualRate = tier.AnnualRate
                });
            }
        }
    }

    public class FeeTierDto
    {
        public decimal LowerBound { get; set; }
        public decimal? UpperBound { get; set; }

        [Range(0, 0.1)]
        public decimal AnnualRate { get; set; }
    }
}
