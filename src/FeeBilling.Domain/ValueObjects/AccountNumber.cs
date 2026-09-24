using System;
using System.Linq;

namespace FeeBilling.Domain.ValueObjects;

/// <summary>
/// Custodian account number. Custodian position files carry it in a fixed-width,
/// 12-character field, so anything longer can't have come from a custodian.
/// </summary>
public sealed class AccountNumber : IEquatable<AccountNumber>
{
    public const int MaxLength = 12;

    private AccountNumber(string value) => Value = value;

    public string Value { get; }

    public static AccountNumber Create(string? value)
    {
        var trimmed = value?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new ArgumentException("Account number is required.", nameof(value));
        }

        if (trimmed!.Length > MaxLength)
        {
            throw new ArgumentException($"Account number '{trimmed}' is longer than {MaxLength} characters.", nameof(value));
        }

        if (!trimmed.All(char.IsLetterOrDigit))
        {
            throw new ArgumentException($"Account number '{trimmed}' contains characters other than letters and digits.", nameof(value));
        }

        return new AccountNumber(trimmed);
    }

    public bool Equals(AccountNumber? other) => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is AccountNumber other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;

    public static bool operator ==(AccountNumber? left, AccountNumber? right) => Equals(left, right);

    public static bool operator !=(AccountNumber? left, AccountNumber? right) => !Equals(left, right);
}
