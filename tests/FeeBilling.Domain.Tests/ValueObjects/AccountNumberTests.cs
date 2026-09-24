using FeeBilling.Domain.ValueObjects;

namespace FeeBilling.Domain.Tests.ValueObjects;

public class AccountNumberTests
{
    [Fact]
    public void Create_TrimsAndUppercases()
    {
        Assert.Equal("MRW000001001", AccountNumber.Create("  mrw000001001 ").Value);
    }

    [Fact]
    public void Create_Empty_Throws()
    {
        Assert.Throws<ArgumentException>(() => AccountNumber.Create("   "));
    }

    [Fact]
    public void Create_LongerThanTwelveCharacters_Throws()
    {
        Assert.Throws<ArgumentException>(() => AccountNumber.Create("MRW0000010011"));
    }

    [Fact]
    public void Create_NonAlphanumeric_Throws()
    {
        Assert.Throws<ArgumentException>(() => AccountNumber.Create("MRW-0001001"));
    }

    [Fact]
    public void Equality_IsByValue()
    {
        Assert.Equal(AccountNumber.Create("LFG000002001"), AccountNumber.Create("lfg000002001"));
    }
}
