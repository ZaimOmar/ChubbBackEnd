using Chubb.Claims.Application;
using Chubb.Claims.Domain;
using Chubb.Claims.Infrastructure.Identifiers;
using Chubb.Claims.Infrastructure.Outbox;
using Chubb.Claims.Infrastructure.Persistence;
using Chubb.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Chubb.Claims.Persistence.Tests;

internal sealed class TestData
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);

    private readonly string _connectionString;
    private readonly UuidV7IdGenerator _ids = new(TimeProvider.System);

    public TestData(string connectionString)
    {
        _connectionString = connectionString;
        Claimant = new Actor(Guid.NewGuid(), new HashSet<string> { Roles.Claimant });
        Officer = new Actor(Guid.NewGuid(), new HashSet<string> { Roles.Officer });
        Manager = new Actor(Guid.NewGuid(), new HashSet<string> { Roles.Manager });
    }

    public Actor Claimant { get; }

    public Actor Officer { get; }

    public Actor Manager { get; }

    public CommandContext As(Actor actor) => new(actor, Now, Guid.NewGuid(), _ids);

    public ClaimsDbContext NewContext() => PostgresFixture.NewContext(_connectionString);

    public async Task<Guid> SubmitAsync(string market = Markets.Singapore)
    {
        await using var db = NewContext();
        var policy = NewPolicy(market);
        db.Set<Policy>().Add(policy);
        await db.SaveChangesAsync();

        await using var transaction = await db.Database.BeginTransactionAsync();
        var repository = new EfClaimRepository(db);
        var number = ClaimNumber.Create(market, 2026, await repository.NextClaimSequenceAsync(market, 2026, default));
        var request = new NewClaim(
            number, market, new DateOnly(2026, 6, 10), "Rear-ended at a junction",
            Money.Create(10_000m, policy.Currency), new MotorDetails("SGX1234A", "Orchard Road"));
        var context = As(Claimant);
        var claim = Claim.Submit(context, request, policy).Value;
        repository.Add(claim);
        await Uow(db).SaveAsync(claim, ClaimCommand.Submit, context, default);
        await transaction.CommitAsync();
        return claim.Id;
    }

    public async Task RunAsync(Guid claimId, ClaimCommand command, Actor actor, Func<Claim, CommandContext, DomainRefusal?> action)
    {
        await using var db = NewContext();
        await RunAsync(db, claimId, command, actor, action);
    }

    public async Task RunAsync(ClaimsDbContext db, Guid claimId, ClaimCommand command, Actor actor, Func<Claim, CommandContext, DomainRefusal?> action)
    {
        var claim = await new EfClaimRepository(db).FindForUpdateAsync(claimId, default) ?? throw new InvalidOperationException();
        var context = As(actor);
        action(claim, context).Should().BeNull();
        await Uow(db).SaveAsync(claim, command, context, default);
    }

    public static EfClaimUnitOfWork Uow(ClaimsDbContext db) => new(db, new OutboxWriter(db));

    private Policy NewPolicy(string market) => new()
    {
        Id = _ids.NewId(),
        Number = $"POL-{Guid.NewGuid():N}"[..20],
        PolicyholderId = Claimant.UserId,
        Market = market,
        PeriodStart = new DateOnly(2026, 1, 1),
        PeriodEnd = new DateOnly(2026, 12, 31),
        Excess = Money.Create(500m, MarketCurrency.For(market)),
        CoverageLimit = Money.Create(50_000m, MarketCurrency.For(market)),
    };
}
