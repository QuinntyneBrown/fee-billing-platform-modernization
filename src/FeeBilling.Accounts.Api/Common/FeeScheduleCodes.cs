using FeeBilling.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FeeBilling.Accounts.Api.Common;

/// <summary>
/// FeeSchedule isn't mapped in the EF Core model yet, so the schedule codes shown on the
/// Accounts screens are read with SQL. Delete this once FeeScheduleConfiguration is applied.
/// </summary>
internal static class FeeScheduleCodes
{
    public static async Task<Dictionary<int, string>> LoadAsync(FeeBillingDbContext db, CancellationToken cancellationToken)
    {
        var rows = await db.Database
            .SqlQuery<FeeScheduleCodeRow>($"SELECT Id, Code FROM dbo.FeeSchedules")
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.Id, r => r.Code);
    }

    private sealed class FeeScheduleCodeRow
    {
        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;
    }
}
