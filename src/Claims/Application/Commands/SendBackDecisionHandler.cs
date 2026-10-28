using Chubb.Claims.Domain;

namespace Chubb.Claims.Application;

public sealed record SendBackDecisionCommand(Guid ClaimId, Guid ProposalId, string Reason);

public sealed class SendBackDecisionHandler(IClaimRepository claims, IClaimUnitOfWork unitOfWork)
{
    public async Task<Claim> HandleAsync(SendBackDecisionCommand command, CommandContext context, CancellationToken cancellationToken)
    {
        var claim = await claims.LoadAsync(command.ClaimId, ClaimCommand.SendBackDecision, context, cancellationToken);
        claim.SendBackDecision(context, command.ProposalId, command.Reason).ThrowIfRefused(claim.Id, context);
        await unitOfWork.SaveAsync(claim, ClaimCommand.SendBackDecision, context, cancellationToken);
        return claim;
    }
}
