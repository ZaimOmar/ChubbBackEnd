namespace Chubb.Claims.Infrastructure.Seeding;

internal enum SeedStage
{
    Submitted,
    PickedUp,
    Assessed,
    Proposed,
    ProposedByOfficerManager,
    ProposedAfterSendBack,
    Approved,
    Rejected,
}

internal sealed record SeedScenario(
    int ClaimantIndex, string Market, bool IsMotor, SeedStage Stage, int DaysAgo, decimal Estimate);
