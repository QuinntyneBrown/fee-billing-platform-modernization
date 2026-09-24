namespace FeeBilling.Domain.Entities;

/// <summary>One band of a tiered or blended schedule.</summary>
public class FeeTier
{
    public int Id { get; set; }

    public int FeeScheduleId { get; set; }

    public decimal LowerBound { get; set; }

    /// <summary>Null means the band has no upper limit.</summary>
    public decimal? UpperBound { get; set; }

    /// <summary>Annual rate as a fraction: 0.0075 = 0.75%. Stored as decimal(9,6).</summary>
    public decimal AnnualRate { get; set; }
}
