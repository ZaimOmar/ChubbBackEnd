namespace Chubb.Claims.Domain;

public sealed class Policy
{
    private string _market = string.Empty;
    private decimal _excess;
    private decimal _coverageLimit;
    private string? _excessCurrency;
    private string? _coverageLimitCurrency;
    private DateOnly _periodStart;
    private DateOnly _periodEnd;

    public required Guid Id { get; init; }

    public required string Number { get; init; }

    public required Guid PolicyholderId { get; init; }

    // The initialisers of Market, the money members and the period may run in any order, so each one re-checks the others.
    public required string Market
    {
        get => _market;
        init
        {
            _market = value;
            Currency = MarketCurrency.For(value);
            EnsureCurrency(_excessCurrency);
            EnsureCurrency(_coverageLimitCurrency);
        }
    }

    public string Currency { get; private set; } = string.Empty;

    public required DateOnly PeriodStart
    {
        get => _periodStart;
        init
        {
            _periodStart = value;
            EnsurePeriodOrder();
        }
    }

    public required DateOnly PeriodEnd
    {
        get => _periodEnd;
        init
        {
            _periodEnd = value;
            EnsurePeriodOrder();
        }
    }

    public required Money Excess
    {
        get => Money.Create(_excess, Currency);
        init
        {
            _excess = value.Amount;
            _excessCurrency = value.Currency;
            EnsureCurrency(value.Currency);
        }
    }

    public required Money CoverageLimit
    {
        get => Money.Create(_coverageLimit, Currency);
        init
        {
            _coverageLimit = value.Amount;
            _coverageLimitCurrency = value.Currency;
            EnsureCurrency(value.Currency);
        }
    }

    public bool Covers(DateOnly date) => date >= PeriodStart && date <= PeriodEnd;

    // Phase 1 cap is the coverage limit per claim only (decision log 16).
    public Money NetPayableFor(Money assessed) => assessed.MinusFlooredAtZero(Excess).CappedAt(CoverageLimit);

    private void EnsureCurrency(string? currency)
    {
        if (currency is not null && Currency.Length > 0 && currency != Currency)
        {
            throw new ArgumentException($"Policy amounts must be in {Currency}, not {currency}.");
        }
    }

    private void EnsurePeriodOrder()
    {
        if (_periodStart != default && _periodEnd != default && _periodStart > _periodEnd)
        {
            throw new ArgumentException("The policy period must not end before it starts.");
        }
    }
}
