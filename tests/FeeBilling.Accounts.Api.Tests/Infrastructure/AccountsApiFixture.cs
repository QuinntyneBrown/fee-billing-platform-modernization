using System.Runtime.InteropServices;
using DotNet.Testcontainers.Builders;
using FeeBilling.DbInit;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;

namespace FeeBilling.Accounts.Api.Tests.Infrastructure;

/// <summary>
/// One SQL Server container per test run, created from the same database/ scripts as local dev,
/// and one Accounts.Api instance in memory pointed at it.
/// </summary>
public sealed class AccountsApiFixture : IAsyncLifetime
{
    // SQL Server 2022 images are amd64-only. On ARM64 (Apple Silicon, Windows on ARM) fall back
    // to Azure SQL Edge. Override with FEEBILLING_TEST_SQL_IMAGE.
    private static readonly string SqlImage =
        Environment.GetEnvironmentVariable("FEEBILLING_TEST_SQL_IMAGE")
        ?? (RuntimeInformation.OSArchitecture == Architecture.Arm64
            ? "mcr.microsoft.com/azure-sql-edge:latest"
            : "mcr.microsoft.com/mssql/server:2022-latest");

    private readonly MsSqlContainer _sql = new MsSqlBuilder(SqlImage)
        // The default wait strategy runs sqlcmd inside the container; azure-sql-edge doesn't ship it.
        .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("SQL Server is now ready for client connections"))
        .Build();

    private WebApplicationFactory<Program>? _factory;

    public string ConnectionString { get; private set; } = string.Empty;

    public WebApplicationFactory<Program> Factory => _factory ?? throw new InvalidOperationException("Fixture not initialized.");

    public async ValueTask InitializeAsync()
    {
        await _sql.StartAsync();

        var master = await WaitForLoginAsync(_sql.GetConnectionString());
        await SqlScriptRunner.ApplyAllAsync(master, SqlScriptRunner.FindDatabaseDirectory(AppContext.BaseDirectory));

        ConnectionString = new SqlConnectionStringBuilder(master) { InitialCatalog = "FeeBilling" }.ConnectionString;

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:FeeBilling", ConnectionString);
            builder.UseSetting("RemoteApp:Url", "http://legacy.invalid");
            builder.UseSetting("RemoteApp:ApiKey", "not-used-in-tests");

            builder.ConfigureTestServices(services =>
            {
                // Replace SystemWebAdapters remote authentication (which calls the legacy app).
                services.AddAuthentication()
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
                services.PostConfigure<AuthenticationOptions>(options =>
                {
                    options.DefaultScheme = TestAuthHandler.SchemeName;
                    options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                });
            });
        });
    }

    public HttpClient CreateAuthenticatedClient(string userName = "maple.ops")
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userName);
        return client;
    }

    public HttpClient CreateAnonymousClient() => Factory.CreateClient();

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await _sql.DisposeAsync();
    }

    // The engine logs "ready" slightly before it accepts logins.
    private static async Task<string> WaitForLoginAsync(string connectionString)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();
                return connectionString;
            }
            catch (SqlException) when (attempt < 30)
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }
    }
}

[CollectionDefinition(Name)]
public sealed class AccountsApiCollection : ICollectionFixture<AccountsApiFixture>
{
    public const string Name = "Accounts API";
}
