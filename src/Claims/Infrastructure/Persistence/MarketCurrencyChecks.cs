namespace Chubb.Claims.Infrastructure.Persistence;

internal static class MarketCurrencyChecks
{
    public const string Markets = "market IN ('SG','HK','MY','TH','ID','AU')";

    public const string Pairs =
        "(market = 'SG' AND currency = 'SGD') OR (market = 'HK' AND currency = 'HKD') OR " +
        "(market = 'MY' AND currency = 'MYR') OR (market = 'TH' AND currency = 'THB') OR " +
        "(market = 'ID' AND currency = 'IDR') OR (market = 'AU' AND currency = 'AUD')";
}
