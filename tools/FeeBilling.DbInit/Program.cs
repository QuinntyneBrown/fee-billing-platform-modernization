// Creates and seeds FeeBilling + FeeBillingReporting on any SQL Server:
// the docker-compose container, SQL Express, LocalDB, ...
//
//   dotnet run --project tools/FeeBilling.DbInit
//   dotnet run --project tools/FeeBilling.DbInit -- --reseed
//   dotnet run --project tools/FeeBilling.DbInit -- --connection "Server=.\SQLEXPRESS;Integrated Security=true;TrustServerCertificate=true"
//
// The connection string must point at master (or have no Database=). FEEBILLING_SQL works too.
using FeeBilling.DbInit;
using Microsoft.Data.SqlClient;

const string DefaultConnection = "Server=localhost,1433;User Id=sa;Password=FeeBilling!Passw0rd;TrustServerCertificate=True;Encrypt=False";

var connectionString = GetOption("--connection") ?? Environment.GetEnvironmentVariable("FEEBILLING_SQL") ?? DefaultConnection;
var reseed = args.Contains("--reseed");
var databaseDirectory = GetOption("--database-dir") ?? SqlScriptRunner.FindDatabaseDirectory(AppContext.BaseDirectory);

var builder = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" };
connectionString = builder.ConnectionString;
Console.WriteLine($"Server: {builder.DataSource}");

await WaitForServerAsync(connectionString);

if (await DatabaseExistsAsync(connectionString))
{
    if (!reseed)
    {
        Console.WriteLine("FeeBilling already exists; nothing to do. Use --reseed to drop and recreate both databases.");
        return 0;
    }

    Console.WriteLine("Dropping FeeBilling and FeeBillingReporting...");
    await SqlScriptRunner.ApplyScriptAsync(connectionString, """
        IF DB_ID('FeeBilling') IS NOT NULL
        BEGIN
            ALTER DATABASE FeeBilling SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
            DROP DATABASE FeeBilling;
        END
        GO
        IF DB_ID('FeeBillingReporting') IS NOT NULL
        BEGIN
            ALTER DATABASE FeeBillingReporting SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
            DROP DATABASE FeeBillingReporting;
        END
        """);
}

await SqlScriptRunner.ApplyAllAsync(connectionString, databaseDirectory, Console.WriteLine);
await PrintRowCountsAsync(connectionString);
Console.WriteLine("Done.");
return 0;

string? GetOption(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static async Task WaitForServerAsync(string connectionString)
{
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            return;
        }
        catch (SqlException) when (attempt < 60)
        {
            Console.WriteLine($"Waiting for SQL Server ({attempt})...");
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }
}

static async Task<bool> DatabaseExistsAsync(string connectionString)
{
    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new SqlCommand("SELECT DB_ID('FeeBilling')", connection);
    return await command.ExecuteScalarAsync() is not DBNull and not null;
}

static async Task PrintRowCountsAsync(string connectionString)
{
    await using var connection = new SqlConnection(new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "FeeBilling" }.ConnectionString);
    await connection.OpenAsync();
    await using var command = new SqlCommand("""
        SELECT 'Firms', COUNT(*) FROM dbo.Firms
        UNION ALL SELECT 'FeeSchedules', COUNT(*) FROM dbo.FeeSchedules
        UNION ALL SELECT 'FeeTiers', COUNT(*) FROM dbo.FeeTiers
        UNION ALL SELECT 'Households', COUNT(*) FROM dbo.Households
        UNION ALL SELECT 'Accounts', COUNT(*) FROM dbo.Accounts
        UNION ALL SELECT 'Positions', COUNT(*) FROM dbo.Positions
        UNION ALL SELECT 'CashFlows', COUNT(*) FROM dbo.CashFlows
        UNION ALL SELECT 'AppUsers', COUNT(*) FROM dbo.AppUsers
        """, connection);
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        Console.WriteLine($"  {reader.GetString(0),-14} {reader.GetInt32(1),6}");
    }
}
