using Chubb.Claims.Domain;

namespace Chubb.Claims.Application;

public sealed record PickUpClaimCommand(Guid ClaimId);

public sealed class PickUpClaimHandler(IClaimRepository claims, IClaimUnitOfWork unitOfWork)
{
    public async Task<Claim> HandleAsync(PickUpClaimCommand command, CommandContext context, CancellationToken cancellationToken)
    {
        var claim = await claims.LoadAsync(command.ClaimId, ClaimCommand.PickUp, context, cancellationToken);
        claim.PickUp(context).ThrowIfRefused(claim.Id, context);
        await unitOfWork.SaveAsync(claim, ClaimCommand.PickUp, context, cancellationToken);
        return claim;
    }
}
