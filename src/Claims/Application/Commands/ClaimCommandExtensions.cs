using Chubb.Claims.Domain;

namespace Chubb.Claims.Application;

internal static class ClaimCommandExtensions
{
    public static async Task<Claim> LoadAsync(
        this IClaimRepository claims, Guid claimId, ClaimCommand command, CommandContext context, CancellationToken cancellationToken)
    {
        var claim = await claims.FindForUpdateAsync(claimId, cancellationToken);
        return claim ?? throw new DomainRefusalException(claimId, context, DomainRefusals.ClaimNotFound(command, claimId));
    }

    public static void ThrowIfRefused(this Result result, Guid claimId, CommandContext context)
    {
        if (result.Refusal is { } refusal)
        {
            throw new DomainRefusalException(claimId, context, refusal);
        }
    }

    public static T ThrowIfRefused<T>(this Result<T> result, Guid claimId, CommandContext context) =>
        result.Refusal is { } refusal
            ? throw new DomainRefusalException(claimId, context, refusal)
            : result.Value;
}
