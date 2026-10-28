using Chubb.Claims.Domain;

namespace Chubb.Claims.Application;

public sealed record AssessClaimCommand(Guid ClaimId, decimal AssessedAmount, string Note);

public sealed class AssessClaimHandler(IClaimRepository claims, IClaimUnitOfWork unitOfWork)
{
    public async Task<Claim> HandleAsync(AssessClaimCommand command, CommandContext context, CancellationToken cancellationToken)
    {
        var claim = await claims.LoadAsync(command.ClaimId, ClaimCommand.Assess, context, cancellationToken);
        var assessed = Money.Create(command.AssessedAmount, claim.Currency);
        claim.Assess(context, assessed, command.Note).ThrowIfRefused(claim.Id, context);
        await unitOfWork.SaveAsync(claim, ClaimCommand.Assess, context, cancellationToken);
        return claim;
    }
}
