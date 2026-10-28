namespace Chubb.Reporting.Domain;

// BR-24: illustrative, static and dated; reporting only, stored amounts are never converted.
public static class FxRates
{
    public static readonly DateOnly AsOf = new(2026, 10, 1);

    private static readonly Dictionary<string, decimal> UsdPerUnit = new()
    {
        ["SGD"] = 0.74m,
        ["HKD"] = 0.128m,
        ["MYR"] = 0.22m,
        ["THB"] = 0.029m,
        ["IDR"] = 0.000062m,
        ["AUD"] = 0.65m,
    };

    public static bool IsSupported(string currency) => UsdPerUnit.ContainsKey(currency);

    public static decimal ToUsd(decimal amount, string currency) =>
        decimal.Round(amount * UsdPerUnit[currency], 2, MidpointRounding.AwayFromZero);
}
