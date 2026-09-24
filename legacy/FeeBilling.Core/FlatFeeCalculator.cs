using System;
using System.Data.Entity;
using System.Linq;
using FeeBilling.Data;
using log4net;

namespace FeeBilling.Core
{
    public static class FlatFeeCalculator
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(FlatFeeCalculator));

        public static decimal CalculateQuarterlyFee(int accountId, DateTime periodEnd)
        {
            var db = DbContextFactory.Current;
            var account = db.Accounts
                .Include("FeeSchedule")
                .Single(a => a.Id == accountId);

            var annual = account.FeeSchedule.FlatAnnualFee ?? 0m;
            var fee = Math.Round(annual / 4, 2);

            Log.Info("Flat fee for " + accountId + " = " + fee);
            return fee;
        }
    }
}
