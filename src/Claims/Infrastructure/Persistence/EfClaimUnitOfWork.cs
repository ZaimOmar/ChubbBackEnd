using Chubb.Claims.Application;
using Chubb.Claims.Domain;
using Microsoft.EntityFrameworkCore;

namespace Chubb.Claims.Infrastructure.Persistence;

public sealed class EfClaimUnitOfWork(ClaimsDbContext db, IOutboxWriter outbox) : IClaimUnitOfWork
{
    public async Task SaveAsync(Claim claim, ClaimCommand command, CommandContext context, CancellationToken cancellationToken)
    {
        var (entries, events) = claim.DrainPending();
        db.Set<TrailEntry>().AddRange(entries);
        if (events.Count > 0)
        {
            outbox.Add(claim.Id, claim.Market, events, context, causationId: entries[0].Id);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DomainRefusalException(claim.Id, context, LostRaceRefusal(command));
        }
    }

    private static DomainRefusal LostRaceRefusal(ClaimCommand command) => command == ClaimCommand.PickUp
        ? DomainRefusals.ClaimAlreadyAssigned(command)
        : DomainRefusals.ConcurrencyConflict(command);
}
