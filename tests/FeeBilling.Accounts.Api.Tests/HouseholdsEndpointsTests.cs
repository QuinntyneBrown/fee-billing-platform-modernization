using System.Net.Http.Json;
using FeeBilling.Accounts.Api.Households;
using FeeBilling.Accounts.Api.Tests.Infrastructure;

namespace FeeBilling.Accounts.Api.Tests;

[Collection(AccountsApiCollection.Name)]
public class HouseholdsEndpointsTests(AccountsApiFixture fixture)
{
    private readonly HttpClient _client = fixture.CreateAuthenticatedClient();

    [Fact]
    public async Task ListHouseholds_ReturnsFirmHouseholdsWithoutMembers()
    {
        var households = await _client.GetFromJsonAsync<List<HouseholdDto>>("/api/households?firmId=1", TestContext.Current.CancellationToken);

        Assert.NotNull(households);
        Assert.Equal(["H-100", "H-200"], households.Select(h => h.HouseholdCode));
        Assert.All(households, h => Assert.Null(h.Members));
        Assert.Equal(3, households[0].MemberCount);
    }

    [Fact]
    public async Task GetHousehold_IncludesMembers()
    {
        var household = await _client.GetFromJsonAsync<HouseholdDto>("/api/households/100", TestContext.Current.CancellationToken);

        Assert.NotNull(household);
        Assert.Equal("HH-TIERED", household.FeeScheduleCode);
        Assert.NotNull(household.Members);
        Assert.Equal([1007, 1008, 1009], household.Members.Select(m => m.Id));
    }
}
