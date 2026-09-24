using System;
using System.Data.SqlClient;
using System.Linq;
using FeeBilling.Data;

namespace FeeBilling.Core
{
    public static class AumService
    {
        /// <summary>
        /// Billable AUM = market value of positions on the period end date, less the cash sleeve
        /// and any securities the firm has excluded from billing.
        /// </summary>
        public static decimal GetBillableAum(int accountId, DateTime periodEnd)
        {
            var db = DbContextFactory.Current;

            // TODO FB-212: large mid-quarter flows should pro-rate. For now we bill on the
            // period-end market value only.
            return db.Database.SqlQuery<decimal>(
                    "EXEC dbo.usp_GetBillableAum @AccountId, @AsOfDate",
                    new SqlParameter("@AccountId", accountId),
                    new SqlParameter("@AsOfDate", periodEnd.Date))
                .Single();
        }
    }
}
