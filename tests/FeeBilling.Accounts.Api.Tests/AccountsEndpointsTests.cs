using System.Net;
using System.Net.Http.Json;
using FeeBilling.Accounts.Api.Accounts;
using FeeBilling.Accounts.Api.Tests.Infrastructure;

namespace FeeBilling.Accounts.Api.Tests;

[Collection(AccountsApiCollection.Name)]
public class AccountsEndpointsTests(AccountsApiFixture fixture)
{
    private readonly HttpClient _client = fixture.CreateAuthenticatedClient();

    [Fact]
    public async Task ListAccounts_ReturnsEveryAccountForTheFirm()
    {
        var accounts = await _client.GetFromJsonAsync<List<AccountDto>>("/api/accounts?firmId=1", TestContext.Current.CancellationToken);

        Assert.NotNull(accounts);
        Assert.Equal(13, accounts.Count);
        Assert.All(accounts, a => Assert.Equal(1, a.FirmId));
    }

    [Fact]
    public async Task ListAccounts_Search_MatchesAccountName()
    {
        var accounts = await _client.GetFromJsonAsync<List<AccountDto>>("/api/accounts?firmId=1&search=Okafor", TestContext.Current.CancellationToken);

        Assert.NotNull(accounts);
        Assert.Equal([1007, 1008, 1009], accounts.Select(a => a.Id).Order());
    }

    [Fact]
    public async Task GetAccount_ReturnsAccountWithItsSchedule()
    {
        var account = await _client.GetFromJsonAsync<AccountDto>("/api/accounts/1001", TestContext.Current.CancellationToken);

        Assert.NotNull(account);
        Assert.Equal("MRW000001001", account.AccountNumber);
        Assert.Equal("STD-TIERED", account.FeeScheduleCode);
        Assert.Null(account.HouseholdCode);
    }

    [Fact]
    public async Task GetAccount_HouseholdMember_ShowsHouseholdSchedule()
    {
        var account = await _client.GetFromJsonAsync<AccountDto>("/api/accounts/1007", TestContext.Current.CancellationToken);

        Assert.NotNull(account);
        Assert.Equal("H-100", account.HouseholdCode);
        Assert.Null(account.FeeScheduleId);
        Assert.Equal("HH-TIERED", account.FeeScheduleCode);
    }

    [Fact]
    public async Task GetAccount_UsesPascalCasePropertyNames()
    {
        // The AngularJS app reads account.AccountNumber, as Web API 2 + Newtonsoft produced it.
        var json = await _client.GetStringAsync("/api/accounts/1001", TestContext.Current.CancellationToken);

        Assert.Contains("\"AccountNumber\":", json);
        Assert.DoesNotContain("\"accountNumber\":", json);
    }

    [Fact]
    public async Task GetAccount_Unknown_Returns404()
    {
        var response = await _client.GetAsync("/api/accounts/999999", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListAccounts_WithoutUser_Returns401()
    {
        var response = await fixture.CreateAnonymousClient().GetAsync("/api/accounts?firmId=1", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
