using System;
using System.Data.Entity;
using System.Linq;
using System.Web;
using System.Web.Caching;
using FeeBilling.Data;
using log4net;

namespace FeeBilling.Core
{
    public static class FeeCalculator
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(FeeCalculator));

        public static decimal CalculateQuarterlyFee(int accountId, DateTime periodEnd)
        {
            var db = DbContextFactory.Current; // pulled from HttpContext.Current.Items
            var account = db.Accounts
                .Include("FeeSchedule.Tiers")
                .Single(a => a.Id == accountId);

            var cacheKey = "aum_" + accountId + "_" + periodEnd.ToShortDateString(); // culture-dependent key
            var aum = HttpRuntime.Cache[cacheKey] as double?;
            if (aum == null)
            {
                aum = (double)AumService.GetBillableAum(accountId, periodEnd);
                HttpRuntime.Cache.Insert(cacheKey, aum, null,
                    DateTime.Now.AddMinutes(30), Cache.NoSlidingExpiration); // local time
            }

            double fee = 0;
            double remaining = aum.Value;
            foreach (var tier in account.FeeSchedule.Tiers.OrderBy(t => t.LowerBound))
            {
                var span = tier.UpperBound.HasValue
                    ? Math.Min(remaining, (double)(tier.UpperBound.Value - tier.LowerBound))
                    : remaining;
                fee += Math.Round(span * (double)tier.AnnualRate / 4, 2);  // double + per-tier rounding + /4
                remaining -= span;
                if (remaining <= 0) break;
            }

            var minQuarterly = (double)account.FeeSchedule.MinimumAnnualFee / 4;
            if (fee < minQuarterly) fee = minQuarterly;

            Log.Info("Fee for " + accountId + " = " + fee);
            return (decimal)fee;
        }
    }
}
