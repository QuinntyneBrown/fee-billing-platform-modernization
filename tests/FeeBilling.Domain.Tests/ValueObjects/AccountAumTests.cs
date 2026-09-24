using FeeBilling.Domain.ValueObjects;

namespace FeeBilling.Domain.Tests.ValueObjects;

public class AccountAumTests
{
    [Fact]
    public void Equality_IsByValue()
    {
        Assert.Equal(new AccountAum(1007, 1_500_000.00m), new AccountAum(1007, 1_500_000m));
    }
}
