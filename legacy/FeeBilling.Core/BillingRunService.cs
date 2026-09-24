using System;
using System.Data.SqlClient;
using System.Linq;
using FeeBilling.Data;
using FeeBilling.Domain.ValueObjects;
using log4net;

namespace FeeBilling.Core
{
    public static class BillingRunService
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(BillingRunService));

        public static int Enqueue(int firmId, DateTime periodEnd, string requestedBy)
        {
            var db = DbContextFactory.Current;
            var runId = db.Database.SqlQuery<int>(
                    "EXEC dbo.usp_EnqueueBillingRun @FirmId, @PeriodEnd, @RequestedBy",
                    new SqlParameter("@FirmId", firmId),
                    new SqlParameter("@PeriodEnd", periodEnd.Date),
                    new SqlParameter("@RequestedBy", (object)requestedBy ?? DBNull.Value))
                .Single();

            Log.Info("Billing run " + runId + " queued for firm " + firmId + " by " + requestedBy);
            return runId;
        }

        /// <summary>
        /// Householded accounts (no schedule of their own) are billed through HouseholdFeeService.
        /// </summary>
        public static bool IsHouseholdBilled(Account account)
        {
            return account.HouseholdId.HasValue && account.FeeScheduleId == null;
        }

        /// <summary>
        /// Quarterly fee for one account that has its own schedule.
        /// </summary>
        public static decimal CalculateAccountFee(int accountId, DateTime periodEnd)
        {
            var db = DbContextFactory.Current;
            var account = db.Accounts.Find(accountId);

            decimal fee;
            switch (account.FeeSchedule.ScheduleType)
            {
                case ScheduleTypes.Blended:
                    fee = BlendedFeeCalculator.CalculateQuarterlyFee(accountId, periodEnd);
                    break;
                case ScheduleTypes.Flat:
                    fee = FlatFeeCalculator.CalculateQuarterlyFee(accountId, periodEnd);
                    break;
                default:
                    fee = FeeCalculator.CalculateQuarterlyFee(accountId, periodEnd);
                    break;
            }

            // FB-178: accounts opened during the quarter only pay for the days they were open.
            var quarter = BillingPeriod.ForQuarterEnding(periodEnd);
            if (account.OpenedOn > quarter.Start)
            {
                var daysOpen = (periodEnd - account.OpenedOn).Days;
                fee = Math.Round(fee * daysOpen / 90m, 2);
                Log.Debug("Pro-rated account " + accountId + " for " + daysOpen + " days: " + fee);
            }

            return fee;
        }
    }
}
