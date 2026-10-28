using Chubb.Claims.Domain;
using Chubb.Contracts;

namespace Chubb.Claims.Infrastructure.Seeding;

// Deterministic by design (no Random): the demo shows the same claims on every fresh start.
internal static class SeedData
{
    private const int ClaimCount = 30;
    private const int DaysBackSpread = 40;
    private const decimal BaseExcess = 500m;
    private const decimal BaseLimit = 50_000m;
    private const decimal BaseEstimate = 10_000m;

    private static readonly Guid[] Claimants = [DevSeed.SeedUserIds.Claimant, DevSeed.SeedUserIds.SecondClaimant];

    private static readonly string[] Markets =
        [Contracts.Markets.Singapore, Contracts.Markets.HongKong, Contracts.Markets.Malaysia, Contracts.Markets.Thailand, Contracts.Markets.Indonesia, Contracts.Markets.Australia];

    private static readonly SeedStage[] Stages =
    [
        .. Enumerable.Repeat(SeedStage.Submitted, 7),
        .. Enumerable.Repeat(SeedStage.PickedUp, 3),
        .. Enumerable.Repeat(SeedStage.Assessed, 4),
        .. Enumerable.Repeat(SeedStage.Proposed, 4),
        SeedStage.ProposedByOfficerManager,
        SeedStage.ProposedAfterSendBack,
        .. Enumerable.Repeat(SeedStage.Approved, 5),
        .. Enumerable.Repeat(SeedStage.Rejected, 5),
    ];

    public static string PolicyNumber(int claimantIndex, string market) => $"POL-{market}-{claimantIndex + 1:D4}";

    public static IEnumerable<Policy> Policies(Func<Guid> newId, DateTimeOffset now) =>
        from claimantIndex in Enumerable.Range(0, Claimants.Length)
        from market in Markets
        select new Policy
        {
            Id = newId(),
            Number = PolicyNumber(claimantIndex, market),
            PolicyholderId = Claimants[claimantIndex],
            Market = market,
            PeriodStart = new DateOnly(now.Year - 1, 1, 1),
            PeriodEnd = new DateOnly(now.Year + 1, 12, 31),
            Excess = Money.Create(BaseExcess * ScaleOf(market), MarketCurrency.For(market)),
            CoverageLimit = Money.Create(BaseLimit * ScaleOf(market), MarketCurrency.For(market)),
        };

    public static IReadOnlyList<SeedScenario> Scenarios() =>
        Enumerable.Range(0, ClaimCount).Select(index =>
        {
            var market = Markets[index % Markets.Length];
            var estimate = (BaseEstimate + (index % 5 * 2_000m)) * ScaleOf(market);
            return new SeedScenario(
                index % Claimants.Length, market, index % 3 != 0, Stages[index], 1 + (index * 37 % DaysBackSpread), estimate);
        }).ToList();

    public static Guid ClaimantId(int claimantIndex) => Claimants[claimantIndex];

    // Local-currency scale so amounts look plausible (IDR is large).
    private static decimal ScaleOf(string market) => market switch
    {
        Contracts.Markets.HongKong => 6m,
        Contracts.Markets.Malaysia => 3m,
        Contracts.Markets.Thailand => 25m,
        Contracts.Markets.Indonesia => 10_000m,
        _ => 1m,
    };
}
