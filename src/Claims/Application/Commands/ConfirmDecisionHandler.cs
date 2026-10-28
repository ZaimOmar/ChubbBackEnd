using Chubb.Claims.Domain;

namespace Chubb.Claims.Application;

public sealed record ConfirmDecisionCommand(Guid ClaimId, Guid ProposalId);

public sealed class ConfirmDecisionHandler(IClaimRepository claims, IClaimUnitOfWork unitOfWork)
{
    public async Task<Claim> HandleAsync(ConfirmDecisionCommand command, CommandContext context, CancellationToken cancellationToken)
    {
        var claim = await claims.LoadAsync(command.ClaimId, ClaimCommand.ConfirmDecision, context, cancellationToken);
        claim.ConfirmDecision(context, command.ProposalId).ThrowIfRefused(claim.Id, context);
        await unitOfWork.SaveAsync(claim, ClaimCommand.ConfirmDecision, context, cancellationToken);
        return claim;
    }
}
