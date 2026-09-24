using System;
using System.Data.Entity;
using System.Linq;
using System.Web;
using System.Web.Caching;
using FeeBilling.Data;
using log4net;

namespace FeeBilling.Core
{
    // Copied from FeeCalculator for the blended schedules (FB-96, 2013).
    // Keep the two in sync!
    public static class BlendedFeeCalculator
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(BlendedFeeCalculator));

        public static decimal CalculateQuarterlyFee(int accountId, DateTime periodEnd)
        {
            var db = DbContextFactory.Current;
            var account = db.Accounts
                .Include("FeeSchedule.Tiers")
                .Single(a => a.Id == accountId);

            var cacheKey = "aum_" + accountId + "_" + periodEnd.ToShortDateString();
            var aum = HttpRuntime.Cache[cacheKey] as double?;
            if (aum == null)
            {
                aum = (double)AumService.GetBillableAum(accountId, periodEnd);
                HttpRuntime.Cache.Insert(cacheKey, aum, null,
                    DateTime.Now.AddMinutes(30), Cache.NoSlidingExpiration);
            }

            // Blended: whole AUM at the rate of the highest tier reached
            double rate = 0;
            foreach (var tier in account.FeeSchedule.Tiers.OrderBy(t => t.LowerBound))
            {
                if (aum.Value > (double)tier.LowerBound || tier.LowerBound == 0)
                {
                    rate = (double)tier.AnnualRate;
                }
            }

            double fee = Math.Round(aum.Value * rate / 4, 2);

            var minQuarterly = (double)account.FeeSchedule.MinimumAnnualFee / 4;
            if (fee < minQuarterly) fee = minQuarterly;

            Log.Info("Blended fee for " + accountId + " = " + fee);
            return (decimal)fee;
        }
    }
}
