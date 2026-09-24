using System;

namespace FeeBilling.Domain.ValueObjects;

/// <summary>
/// An inclusive date range that is billed as one period (normally a calendar quarter).
/// Times are ignored; only the date parts are kept.
/// </summary>
public sealed class BillingPeriod : IEquatable<BillingPeriod>
{
    public BillingPeriod(DateTime start, DateTime end)
    {
        if (end.Date < start.Date)
        {
            throw new ArgumentException($"Period end {end:yyyy-MM-dd} is before period start {start:yyyy-MM-dd}.", nameof(end));
        }

        Start = start.Date;
        End = end.Date;
    }

    public DateTime Start { get; }

    public DateTime End { get; }

    /// <summary>Number of calendar days in the period, counting both the start and end dates.</summary>
    public int Days => (End - Start).Days + 1;

    /// <summary>The calendar quarter that ends on (or contains) <paramref name="periodEnd"/>.</summary>
    public static BillingPeriod ForQuarterEnding(DateTime periodEnd)
    {
        var quarterStartMonth = ((periodEnd.Month - 1) / 3 * 3) + 1;
        var start = new DateTime(periodEnd.Year, quarterStartMonth, 1);
        var end = start.AddMonths(3).AddDays(-1);
        return new BillingPeriod(start, end);
    }

    public bool Contains(DateTime date) => date.Date >= Start && date.Date <= End;

    public bool Equals(BillingPeriod? other) => other is not null && Start == other.Start && End == other.End;

    public override bool Equals(object? obj) => obj is BillingPeriod other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            return (Start.GetHashCode() * 397) ^ End.GetHashCode();
        }
    }

    public override string ToString() => $"{Start:yyyy-MM-dd}..{End:yyyy-MM-dd}";
}
