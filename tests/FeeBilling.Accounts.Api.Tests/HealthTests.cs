using System.Net;
using FeeBilling.Accounts.Api.Tests.Infrastructure;

namespace FeeBilling.Accounts.Api.Tests;

[Collection(AccountsApiCollection.Name)]
public class HealthTests(AccountsApiFixture fixture)
{
    [Fact]
    public async Task Health_IncludingDatabase_IsHealthy()
    {
        var response = await fixture.CreateAnonymousClient().GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
