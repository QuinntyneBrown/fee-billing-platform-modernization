using FeeBilling.Domain.Entities;

namespace FeeBilling.Accounts.Api.Accounts;

/// <summary>Same shape as the legacy Web API 2 AccountDto (property names and order).</summary>
public sealed class AccountDto
{
    public int Id { get; init; }

    public int FirmId { get; init; }

    public string AccountNumber { get; init; } = string.Empty;

    public string AccountName { get; init; } = string.Empty;

    public string CustodianCode { get; init; } = string.Empty;

    public string Currency { get; init; } = string.Empty;

    public int? HouseholdId { get; init; }

    public string? HouseholdCode { get; init; }

    public int? FeeScheduleId { get; init; }

    public string? FeeScheduleCode { get; init; }

    public DateTime OpenedOn { get; init; }

    public DateTime? ClosedOn { get; init; }

    public bool IsActive { get; init; }

    public DateTime? LastValuedAt { get; init; }

    public static AccountDto From(Account account, IReadOnlyDictionary<int, string> scheduleCodes)
    {
        // Accounts billed through a household show the household's schedule.
        var scheduleId = account.FeeScheduleId ?? account.Household?.FeeScheduleId;

        return new AccountDto
        {
            Id = account.Id,
            FirmId = account.FirmId,
            AccountNumber = account.AccountNumber,
            AccountName = account.AccountName,
            CustodianCode = account.CustodianCode,
            Currency = account.Currency,
            HouseholdId = account.HouseholdId,
            HouseholdCode = account.Household?.HouseholdCode,
            FeeScheduleId = account.FeeScheduleId,
            FeeScheduleCode = scheduleId is { } id && scheduleCodes.TryGetValue(id, out var code) ? code : null,
            OpenedOn = account.OpenedOn,
            ClosedOn = account.ClosedOn,
            IsActive = account.IsActive,
            LastValuedAt = account.LastValuedAt,
        };
    }
}
