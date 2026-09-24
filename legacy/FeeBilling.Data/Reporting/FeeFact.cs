using System;

namespace FeeBilling.Data.Reporting
{
    public class FeeFact
    {
        public long Id { get; set; }
        public int RunId { get; set; }
        public int FirmId { get; set; }
        public int AccountId { get; set; }
        public int? HouseholdId { get; set; }
        public DateTime PeriodEnd { get; set; }
        public string ScheduleCode { get; set; }
        public decimal? BillableAum { get; set; }
        public decimal FeeAmount { get; set; }
        public DateTime LoadedOn { get; set; }
    }
}
