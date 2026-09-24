using System;

namespace FeeBilling.Core
{
    // Predates FeeBilling.Domain.BillingPeriod; still used by the Web project and the reports.
    public static class QuarterHelper
    {
        public static DateTime GetQuarterStart(DateTime date)
        {
            var month = ((date.Month - 1) / 3) * 3 + 1;
            return new DateTime(date.Year, month, 1);
        }

        public static DateTime GetQuarterEnd(DateTime date)
        {
            return GetQuarterStart(date).AddMonths(3).AddDays(-1);
        }

        public static bool IsQuarterEnd(DateTime date)
        {
            return date.Date == GetQuarterEnd(date);
        }

        public static int DaysInQuarter(DateTime date)
        {
            return (GetQuarterEnd(date) - GetQuarterStart(date)).Days + 1;
        }

        /// <summary>The most recent quarter end on or before today (server local time).</summary>
        public static DateTime LastQuarterEnd()
        {
            var today = DateTime.Now.Date;
            return IsQuarterEnd(today) ? today : GetQuarterStart(today).AddDays(-1);
        }

        public static string GetQuarterLabel(DateTime date)
        {
            return "Q" + ((date.Month - 1) / 3 + 1) + " " + date.Year;
        }
    }
}
