using System;

namespace FeeBilling.Web.Models
{
    public class BillingRunRequest
    {
        public int FirmId { get; set; }
        public DateTime PeriodEnd { get; set; }
    }
}
