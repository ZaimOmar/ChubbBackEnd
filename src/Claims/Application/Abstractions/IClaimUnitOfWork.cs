using Chubb.Claims.Domain;

namespace Chubb.Claims.Application;

public interface IClaimUnitOfWork
{
    /// <summary>
    /// Writes the claim change, its pending trail entries and its outbox rows in one SaveChanges.
    /// A lost race becomes a <see cref="DomainRefusalException"/>; the caller owns the transaction.
    /// </summary>
    Task SaveAsync(Claim claim, ClaimCommand command, CommandContext context, CancellationToken cancellationToken);
}
