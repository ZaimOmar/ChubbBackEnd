using Chubb.Claims.Application;
using Chubb.Claims.Domain;
using Microsoft.EntityFrameworkCore;

namespace Chubb.Claims.Infrastructure.Persistence;

public sealed class EfClaimRepository(ClaimsDbContext db) : IClaimRepository
{
    public Task<Claim?> FindForUpdateAsync(Guid claimId, CancellationToken cancellationToken) =>
        db.Set<Claim>()
            .Include(claim => claim.Policy)
            .Include(claim => claim.Proposals)
            .SingleOrDefaultAsync(claim => claim.Id == claimId, cancellationToken);

    public Task<Policy?> FindPolicyByNumberAsync(string policyNumber, CancellationToken cancellationToken) =>
        db.Set<Policy>().SingleOrDefaultAsync(policy => policy.Number == policyNumber, cancellationToken);

    // ToListAsync, not SingleAsync: EF would wrap the INSERT in a subquery to compose the single-row operator.
    public async Task<int> NextClaimSequenceAsync(string market, int year, CancellationToken cancellationToken)
    {
        var values = await db.Database.SqlQuery<int>($"""
            INSERT INTO claims.claim_number_sequences (market, year, last_value)
            VALUES ({market}, {year}, 1)
            ON CONFLICT (market, year) DO UPDATE SET last_value = claim_number_sequences.last_value + 1
            RETURNING last_value AS "Value"
            """).ToListAsync(cancellationToken);
        return values.Single();
    }

    public void Add(Claim claim) => db.Set<Claim>().Add(claim);
}
