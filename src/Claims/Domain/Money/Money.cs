using System.Text.RegularExpressions;

namespace Chubb.Claims.Domain;

public readonly partial record struct Money
{
    private const int MaxDecimalPlaces = 2;

    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }

    public string Currency { get; }

    public bool IsPositive => Amount > 0;

    public static Money Create(decimal amount, string currency)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Money cannot be negative.");
        }

        if (decimal.Round(amount, MaxDecimalPlaces) != amount)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Money allows at most 2 decimal places.");
        }

        if (!IsoCurrencyPattern().IsMatch(currency))
        {
            throw new ArgumentException("Currency must be an ISO 4217 code.", nameof(currency));
        }

        return new Money(amount, currency);
    }

    public static Money Zero(string currency) => Create(0m, currency);

    public Money MinusFlooredAtZero(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Math.Max(Amount - other.Amount, 0m), Currency);
    }

    public Money CappedAt(Money limit)
    {
        EnsureSameCurrency(limit);
        return new Money(Math.Min(Amount, limit.Amount), Currency);
    }

    public bool IsGreaterThan(Money other)
    {
        EnsureSameCurrency(other);
        return Amount > other.Amount;
    }

    private void EnsureSameCurrency(Money other)
    {
        if (Currency != other.Currency)
        {
            throw new InvalidOperationException($"Cannot combine {Currency} with {other.Currency}.");
        }
    }

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex IsoCurrencyPattern();
}
