using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using FeeBilling.Data;
using FeeBilling.Domain.ValueObjects;
using log4net;

namespace FeeBilling.Core
{
    // Householding (FB-150, 2015). Written in decimal, unlike FeeCalculator - the double
    // version gave "funny cents" on the household statements.
    public static class HouseholdFeeService
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(HouseholdFeeService));

        public static decimal CalculateHouseholdQuarterlyFee(int householdId, DateTime periodEnd, out IList<AccountAum> members)
        {
            var db = DbContextFactory.Current;
            var household = db.Households
                .Include("FeeSchedule.Tiers")
                .Single(h => h.Id == householdId);

            members = household.Accounts
                .Where(a => a.IsActive)
                .Select(a => new AccountAum(a.Id, AumService.GetBillableAum(a.Id, periodEnd)))
                .ToList();

            var total = members.Sum(m => m.BillableAum);

            decimal fee = 0;
            decimal remaining = total;
            foreach (var tier in household.FeeSchedule.Tiers.OrderBy(t => t.LowerBound))
            {
                var span = tier.UpperBound.HasValue
                    ? Math.Min(remaining, tier.UpperBound.Value - tier.LowerBound)
                    : remaining;
                fee += Math.Round(span * tier.AnnualRate / 4, 2);   // same per-tier rounding as FeeCalculator
                remaining -= span;
                if (remaining <= 0) break;
            }

            var minQuarterly = household.FeeSchedule.MinimumAnnualFee / 4;
            if (fee < minQuarterly) fee = minQuarterly;

            Log.Info("Household fee for " + household.HouseholdCode + " = " + fee);
            return fee;
        }

        public static Dictionary<int, decimal> CalculateAndAllocate(int householdId, DateTime periodEnd)
        {
            IList<AccountAum> members;
            var householdFee = CalculateHouseholdQuarterlyFee(householdId, periodEnd, out members);
            return HouseholdAllocator.Allocate(householdFee, members);
        }
    }
}
