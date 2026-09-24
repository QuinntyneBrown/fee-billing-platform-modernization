using FeeBilling.Accounts.Api.Accounts;
using FeeBilling.Domain.Entities;

namespace FeeBilling.Accounts.Api.Households;

/// <summary>Same shape as the legacy Web API 2 HouseholdDto. Members is null in list responses.</summary>
public sealed class HouseholdDto
{
    public int Id { get; init; }

    public int FirmId { get; init; }

    public string HouseholdCode { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public int? FeeScheduleId { get; init; }

    public string? FeeScheduleCode { get; init; }

    public int MemberCount { get; init; }

    public List<AccountDto>? Members { get; init; }

    public static HouseholdDto From(Household household, IReadOnlyDictionary<int, string> scheduleCodes, bool includeMembers) => new()
    {
        Id = household.Id,
        FirmId = household.FirmId,
        HouseholdCode = household.HouseholdCode,
        Name = household.Name,
        FeeScheduleId = household.FeeScheduleId,
        FeeScheduleCode = household.FeeScheduleId is { } id && scheduleCodes.TryGetValue(id, out var code) ? code : null,
        MemberCount = household.Accounts.Count,
        Members = includeMembers
            ? household.Accounts.OrderBy(a => a.Id).Select(a => AccountDto.From(a, scheduleCodes)).ToList()
            : null,
    };
}
