using System;

namespace FeeBilling.Domain.Entities;

public class Account
{
    public int Id { get; set; }

    public int FirmId { get; set; }

    /// <summary>Custodian account number (max 12 characters). See <see cref="ValueObjects.AccountNumber"/>.</summary>
    public string AccountNumber { get; set; } = string.Empty;

    public string AccountName { get; set; } = string.Empty;

    public string CustodianCode { get; set; } = string.Empty;

    public string Currency { get; set; } = "CAD";

    public int? HouseholdId { get; set; }

    /// <summary>Null when the account is billed through its household's schedule.</summary>
    public int? FeeScheduleId { get; set; }

    public DateTime OpenedOn { get; set; }

    public DateTime? ClosedOn { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>When the nightly valuation job last priced this account (stored in UTC).</summary>
    public DateTime? LastValuedAt { get; set; }

    public DateTime CreatedOn { get; set; }

    public Firm? Firm { get; set; }

    public Household? Household { get; set; }

    public FeeSchedule? FeeSchedule { get; set; }
}
