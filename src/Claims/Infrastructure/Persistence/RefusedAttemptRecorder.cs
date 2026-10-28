using Chubb.Claims.Application;
using Chubb.Claims.Domain;

namespace Chubb.Claims.Infrastructure.Persistence;

public sealed class RefusedAttemptRecorder(ClaimsDbContext db) : IRefusedAttemptRecorder
{
    public async Task RecordAsync(DomainRefusalException refusal, CancellationToken cancellationToken)
    {
        var entry = TrailEntry.Refused(refusal.ClaimId, refusal.Context, refusal.Refusal);
        db.Set<TrailEntry>().Add(entry);
        await db.SaveChangesAsync(cancellationToken);
    }
}
