using System;

namespace FeeBilling.Domain.ValueObjects;

/// <summary>
/// An account's billable AUM (after exclusions) on the billing date.
/// Legacy HouseholdAllocator consumes this type, so it has to stay C# 7.3-friendly.
/// </summary>
public sealed class AccountAum : IEquatable<AccountAum>
{
    public AccountAum(int accountId, decimal billableAum)
    {
        AccountId = accountId;
        BillableAum = billableAum;
    }

    public int AccountId { get; }

    public decimal BillableAum { get; }

    public bool Equals(AccountAum? other) => other is not null && AccountId == other.AccountId && BillableAum == other.BillableAum;

    public override bool Equals(object? obj) => obj is AccountAum other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            return (AccountId * 397) ^ BillableAum.GetHashCode();
        }
    }

    public override string ToString() => $"{AccountId}: {BillableAum}";
}
