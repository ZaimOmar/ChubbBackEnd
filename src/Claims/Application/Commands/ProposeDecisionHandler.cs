using Chubb.Claims.Domain;

namespace Chubb.Claims.Application;

public sealed record ProposeDecisionCommand(Guid ClaimId, DecisionProposal Proposal);

public sealed class ProposeDecisionHandler(IClaimRepository claims, IClaimUnitOfWork unitOfWork)
{
    public async Task<Claim> HandleAsync(ProposeDecisionCommand command, CommandContext context, CancellationToken cancellationToken)
    {
        var claim = await claims.LoadAsync(command.ClaimId, ClaimCommand.ProposeDecision, context, cancellationToken);
        claim.ProposeDecision(context, command.Proposal).ThrowIfRefused(claim.Id, context);
        await unitOfWork.SaveAsync(claim, ClaimCommand.ProposeDecision, context, cancellationToken);
        return claim;
    }
}
