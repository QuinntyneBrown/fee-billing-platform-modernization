using System;
using FeeBilling.Data;

namespace FeeBilling.Web.Models
{
    public class AccountDto
    {
        public int Id { get; set; }
        public int FirmId { get; set; }
        public string AccountNumber { get; set; }
        public string AccountName { get; set; }
        public string CustodianCode { get; set; }
        public string Currency { get; set; }
        public int? HouseholdId { get; set; }
        public string HouseholdCode { get; set; }
        public int? FeeScheduleId { get; set; }
        public string FeeScheduleCode { get; set; }
        public DateTime OpenedOn { get; set; }
        public DateTime? ClosedOn { get; set; }
        public bool IsActive { get; set; }
        public DateTime? LastValuedAt { get; set; }

        public static AccountDto From(Account a)
        {
            return new AccountDto
            {
                Id = a.Id,
                FirmId = a.FirmId,
                AccountNumber = a.AccountNumber,
                AccountName = a.AccountName,
                CustodianCode = a.CustodianCode,
                Currency = a.Currency,
                HouseholdId = a.HouseholdId,
                HouseholdCode = a.Household != null ? a.Household.HouseholdCode : null,
                FeeScheduleId = a.FeeScheduleId,
                FeeScheduleCode = a.FeeSchedule != null
                    ? a.FeeSchedule.Code
                    : (a.Household != null && a.Household.FeeSchedule != null ? a.Household.FeeSchedule.Code : null),
                OpenedOn = a.OpenedOn,
                ClosedOn = a.ClosedOn,
                IsActive = a.IsActive,
                // LastValuedAt is stored in UTC. Mark it so the JSON carries a trailing 'Z' and the
                // browser converts it to local time. Without this, users west of UTC see the next
                // day's date after 8 p.m. (FB-288)
                LastValuedAt = a.LastValuedAt.HasValue
                    ? DateTime.SpecifyKind(a.LastValuedAt.Value, DateTimeKind.Utc)
                    : (DateTime?)null
            };
        }
    }
}
