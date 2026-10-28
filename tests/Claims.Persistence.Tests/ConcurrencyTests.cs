using Chubb.Claims.Application;
using Chubb.Claims.Domain;
using Chubb.Claims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Chubb.Claims.Persistence.Tests;

[Collection(PostgresCollection.Name)]
public sealed class ConcurrencyTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Claim_StaleUpdate_ThrowsConcurrencyOnXmin()
    {
        var data = new TestData(await postgres.CreateMigratedDatabaseAsync());
        var claimId = await data.SubmitAsync();
        await using var stale = data.NewContext();
        var staleClaim = await new EfClaimRepository(stale).FindForUpdateAsync(claimId, default);
        await data.RunAsync(claimId, ClaimCommand.PickUp, data.Officer, (claim, context) => claim.PickUp(context).Refusal);

        staleClaim!.PickUp(data.As(data.Officer)).IsSuccess.Should().BeTrue();
        var save = () => stale.SaveChangesAsync();

        await save.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task UnitOfWork_LostPickUpRace_ThrowsClaimAlreadyAssigned()
    {
        var data = new TestData(await postgres.CreateMigratedDatabaseAsync());
        var claimId = await data.SubmitAsync();
        await using var loser = data.NewContext();
        var loserClaim = await new EfClaimRepository(loser).FindForUpdateAsync(claimId, default);
        await data.RunAsync(claimId, ClaimCommand.PickUp, data.Officer, (claim, context) => claim.PickUp(context).Refusal);
        var otherOfficer = new Actor(Guid.NewGuid(), data.Officer.Roles);
        var context = data.As(otherOfficer);
        loserClaim!.PickUp(context).IsSuccess.Should().BeTrue();

        var save = () => TestData.Uow(loser).SaveAsync(loserClaim, ClaimCommand.PickUp, context, default);

        var refusal = (await save.Should().ThrowAsync<DomainRefusalException>()).Which;
        refusal.Refusal.Type.Should().Be(DomainRefusals.Types.ClaimAlreadyAssigned);
        refusal.ClaimId.Should().Be(claimId);
    }

    [Fact]
    public async Task UnitOfWork_LostRaceOnOtherCommand_ThrowsConcurrencyConflict()
    {
        var data = new TestData(await postgres.CreateMigratedDatabaseAsync());
        var claimId = await data.SubmitAsync();
        await data.RunAsync(claimId, ClaimCommand.PickUp, data.Officer, (claim, context) => claim.PickUp(context).Refusal);
        await using var loser = data.NewContext();
        var loserClaim = await new EfClaimRepository(loser).FindForUpdateAsync(claimId, default);
        await data.RunAsync(claimId, ClaimCommand.Assess, data.Officer, (claim, context) =>
            claim.Assess(context, Money.Create(8_000m, claim.Currency), "Inspected").Refusal);
        var context = data.As(data.Officer);
        loserClaim!.Assess(context, Money.Create(7_000m, loserClaim.Currency), "Again").IsSuccess.Should().BeTrue();

        var save = () => TestData.Uow(loser).SaveAsync(loserClaim, ClaimCommand.Assess, context, default);

        (await save.Should().ThrowAsync<DomainRefusalException>()).Which.Refusal.Type
            .Should().Be(DomainRefusals.Types.ConcurrencyConflict);
    }
}
