using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace FeeBilling.DbInit;

/// <summary>
/// Applies the scripts under <c>database/</c> in order, splitting on sqlcmd-style <c>GO</c> lines.
/// Shared with the Testcontainers fixture in tests/FeeBilling.Accounts.Api.Tests (linked source).
/// </summary>
public static partial class SqlScriptRunner
{
    /// <summary>Scripts in the order they must run, relative to the <c>database/</c> directory.</summary>
    public static readonly IReadOnlyList<string> Scripts =
    [
        "billing/000-create-databases.sql",
        "billing/001-schema.sql",
        "billing/002-stored-procedures.sql",
        "reporting/001-schema.sql",
        "seed/010-seed-q3-2026.sql",
    ];

    public static async Task ApplyAllAsync(string masterConnectionString, string databaseDirectory, Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        foreach (var script in Scripts)
        {
            log?.Invoke($"Applying {script}");
            var path = Path.Combine(databaseDirectory, script.Replace('/', Path.DirectorySeparatorChar));
            await ApplyScriptAsync(masterConnectionString, await File.ReadAllTextAsync(path, cancellationToken), cancellationToken);
        }
    }

    public static async Task ApplyScriptAsync(string connectionString, string script, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        foreach (var batch in SplitBatches(script))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = batch;
            command.CommandTimeout = 300;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public static IEnumerable<string> SplitBatches(string script) =>
        GoSeparator().Split(script).Where(batch => !string.IsNullOrWhiteSpace(batch));

    /// <summary>Walks up from <paramref name="startDirectory"/> until it finds the repo's <c>database/</c> folder.</summary>
    public static string FindDatabaseDirectory(string startDirectory)
    {
        for (var dir = new DirectoryInfo(startDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "database", "billing");
            if (Directory.Exists(candidate))
            {
                return Path.Combine(dir.FullName, "database");
            }
        }

        throw new DirectoryNotFoundException($"Could not find a 'database/billing' directory above '{startDirectory}'.");
    }

    [GeneratedRegex(@"^\s*GO\s*;?\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex GoSeparator();
}
