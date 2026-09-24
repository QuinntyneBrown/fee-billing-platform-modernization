using System;
using System.Collections.Generic;

namespace FeeBilling.Domain.Entities;

/// <summary>
/// Linked accounts (usually a family) whose AUM is combined to reach lower-rate tiers.
/// The household fee is allocated back to the member accounts.
/// </summary>
public class Household
{
    public int Id { get; set; }

    public int FirmId { get; set; }

    public string HouseholdCode { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public int? FeeScheduleId { get; set; }

    public DateTime CreatedOn { get; set; }

    public Firm? Firm { get; set; }

    public FeeSchedule? FeeSchedule { get; set; }

    public ICollection<Account> Accounts { get; set; } = new List<Account>();
}
