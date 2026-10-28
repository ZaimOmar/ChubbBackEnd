using Chubb.Claims.Domain;
using Chubb.Claims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Chubb.Claims.Persistence.Tests;

[Collection(PostgresCollection.Name)]
public sealed class PersistenceRoundTripTests(PostgresFixture postgres)
{
    [Fact]
    public async Task ProposeAndConfirm_Persisted_WritesProposalTrailAndOutboxAndReloadsAggregate()
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();
        var data = new TestData(connectionString);
        var claimId = await data.SubmitAsync();
        await data.RunAsync(claimId, ClaimCommand.PickUp, data.Officer, (claim, context) => claim.PickUp(context).Refusal);
        await data.RunAsync(claimId, ClaimCommand.Assess, data.Officer, (claim, context) =>
            claim.Assess(context, Money.Create(8_000m, claim.Currency), "Inspected").Refusal);
        var proposalId = Guid.Empty;
        await data.RunAsync(claimId, ClaimCommand.ProposeDecision, data.Officer, (claim, context) =>
        {
            var result = claim.ProposeDecision(context, new DecisionProposal(ProposalOutcome.Approve, null, "Repair cost verified", null));
            proposalId = result.IsSuccess ? result.Value : Guid.Empty;
            return result.Refusal;
        });
        await data.RunAsync(claimId, ClaimCommand.ConfirmDecision, data.Manager, (claim, context) =>
            claim.ConfirmDecision(context, proposalId).Refusal);

        await using var db = data.NewContext();
        var reloaded = await new EfClaimRepository(db).FindForUpdateAsync(claimId, default);
        reloaded!.Status.Should().Be(ClaimStatus.Approved);
        reloaded.Details.Should().BeEquivalentTo(new MotorDetails("SGX1234A", "Orchard Road"));
        reloaded.Payable.Should().Be(Money.Create(7_500m, "SGD"));
        reloaded.Proposals.Should().ContainSingle().Which.State.Should().Be(ProposalState.Confirmed);
        reloaded.Number.Market.Should().Be("SG");
        (await Count(connectionString, "claim_history")).Should().Be(7);
        (await Count(connectionString, "outbox")).Should().Be(5);
    }

    [Fact]
    public async Task Policy_Persisted_ReloadsMoneyWithMarketCurrency()
    {
        var data = new TestData(await postgres.CreateMigratedDatabaseAsync());
        await data.SubmitAsync("ID");

        await using var db = data.NewContext();
        var policy = await db.Set<Policy>().SingleAsync();

        policy.Excess.Should().Be(Money.Create(500m, "IDR"));
        policy.CoverageLimit.Currency.Should().Be("IDR");
    }

    private static Task<long> Count(string connectionString, string table) =>
        Sql.ScalarAsync<long>(connectionString, $"SELECT count(*) FROM claims.{table}");
}
