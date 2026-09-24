using FeeBilling.Domain.ValueObjects;

namespace FeeBilling.Domain.Tests.ValueObjects;

public class BillingPeriodTests
{
    [Fact]
    public void ForQuarterEnding_Q3_2026_StartsJulyFirst()
    {
        var period = BillingPeriod.ForQuarterEnding(new DateTime(2026, 9, 30));

        Assert.Equal(new DateTime(2026, 7, 1), period.Start);
        Assert.Equal(new DateTime(2026, 9, 30), period.End);
    }

    [Fact]
    public void Days_Q3_2026_Is92()
    {
        Assert.Equal(92, BillingPeriod.ForQuarterEnding(new DateTime(2026, 9, 30)).Days);
    }

    [Fact]
    public void Days_Q1_LeapYear_Is91()
    {
        Assert.Equal(91, BillingPeriod.ForQuarterEnding(new DateTime(2024, 3, 31)).Days);
    }

    [Fact]
    public void Constructor_EndBeforeStart_Throws()
    {
        Assert.Throws<ArgumentException>(() => new BillingPeriod(new DateTime(2026, 9, 30), new DateTime(2026, 7, 1)));
    }

    [Fact]
    public void Contains_IsInclusiveOfBothEnds()
    {
        var period = BillingPeriod.ForQuarterEnding(new DateTime(2026, 9, 30));

        Assert.True(period.Contains(new DateTime(2026, 7, 1)));
        Assert.True(period.Contains(new DateTime(2026, 9, 30, 23, 59, 59)));
        Assert.False(period.Contains(new DateTime(2026, 10, 1)));
    }

    [Fact]
    public void Equality_IgnoresTimeOfDay()
    {
        var a = new BillingPeriod(new DateTime(2026, 7, 1, 9, 30, 0), new DateTime(2026, 9, 30));
        var b = new BillingPeriod(new DateTime(2026, 7, 1), new DateTime(2026, 9, 30, 17, 0, 0));

        Assert.Equal(a, b);
    }
}
