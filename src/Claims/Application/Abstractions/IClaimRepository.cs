using Chubb.Claims.Domain;

namespace Chubb.Claims.Application;

public interface IClaimRepository
{
    /// <summary>Loads the claim with its policy and every proposal, tracked for update; the trail is not loaded.</summary>
    Task<Claim?> FindForUpdateAsync(Guid claimId, CancellationToken cancellationToken);

    Task<Policy?> FindPolicyByNumberAsync(string policyNumber, CancellationToken cancellationToken);

    /// <summary>Takes the next number of the market-year; the row lock lasts until the surrounding transaction ends.</summary>
    Task<int> NextClaimSequenceAsync(string market, int year, CancellationToken cancellationToken);

    void Add(Claim claim);
}
