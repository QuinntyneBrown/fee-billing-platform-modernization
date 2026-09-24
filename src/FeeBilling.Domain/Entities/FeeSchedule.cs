using System;
using System.Collections.Generic;

namespace FeeBilling.Domain.Entities;

public class FeeSchedule
{
    public int Id { get; set; }

    /// <summary>Null for platform-wide templates.</summary>
    public int? FirmId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public FeeScheduleType ScheduleType { get; set; }

    /// <summary>True when the schedule is applied to combined household AUM.</summary>
    public bool IsHousehold { get; set; }

    public decimal MinimumAnnualFee { get; set; }

    /// <summary>Only used by <see cref="FeeScheduleType.Flat"/> schedules.</summary>
    public decimal? FlatAnnualFee { get; set; }

    public string Currency { get; set; } = "CAD";

    public bool IsActive { get; set; } = true;

    public DateTime CreatedOn { get; set; }

    public DateTime? ModifiedOn { get; set; }

    public string? ModifiedBy { get; set; }

    public ICollection<FeeTier> Tiers { get; set; } = new List<FeeTier>();
}
