using System;

namespace FeeBilling.Invoicing
{
    public class InvoiceDocument
    {
        public int InvoiceId { get; set; }
        public string FirmName { get; set; }
        public string AccountNumber { get; set; }
        public string AccountName { get; set; }
        public string HouseholdCode { get; set; }
        public DateTime PeriodEnd { get; set; }
        public decimal Amount { get; set; }
        public string ScheduleCode { get; set; }
    }
}
