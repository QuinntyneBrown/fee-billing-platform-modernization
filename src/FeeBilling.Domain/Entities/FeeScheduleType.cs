namespace FeeBilling.Domain.Entities;

/// <summary>Stored in FeeSchedules.ScheduleType as 'TIERED' | 'BLENDED' | 'FLAT'.</summary>
public enum FeeScheduleType
{
    /// <summary>Each band of AUM is charged its own rate (marginal).</summary>
    Tiered = 1,

    /// <summary>The whole AUM is charged at the rate of the highest band reached (cliff).</summary>
    Blended = 2,

    /// <summary>Fixed annual amount regardless of AUM.</summary>
    Flat = 3,
}
