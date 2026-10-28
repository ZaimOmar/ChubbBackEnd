using System.Collections.Frozen;
using Chubb.Contracts;

namespace Chubb.Claims.Domain;

public static class MarketCurrency
{
    private static readonly FrozenDictionary<string, string> CurrencyByMarket = new Dictionary<string, string>
    {
        [Markets.Singapore] = "SGD",
        [Markets.HongKong] = "HKD",
        [Markets.Malaysia] = "MYR",
        [Markets.Thailand] = "THB",
        [Markets.Indonesia] = "IDR",
        [Markets.Australia] = "AUD",
    }.ToFrozenDictionary();

    public static string For(string market) => CurrencyByMarket.TryGetValue(market, out var currency)
        ? currency
        : throw new ArgumentException($"Unknown market '{market}'.", nameof(market));
}
