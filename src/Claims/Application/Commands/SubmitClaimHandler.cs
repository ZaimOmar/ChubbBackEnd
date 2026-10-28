using Chubb.Claims.Domain;

namespace Chubb.Claims.Application;

public sealed class SubmitClaimHandler(IClaimRepository claims, IClaimUnitOfWork unitOfWork)
{
    public async Task<Claim> HandleAsync(SubmitClaimCommand command, CommandContext context, CancellationToken cancellationToken)
    {
        var policy = await claims.FindPolicyByNumberAsync(command.PolicyNumber, cancellationToken)
            ?? throw new DomainRefusalException(Guid.Empty, context, DomainRefusals.PolicyNotFound(ClaimCommand.Submit));
        var year = context.Now.UtcDateTime.Year;
        var sequence = await claims.NextClaimSequenceAsync(policy.Market, year, cancellationToken);
        var request = new NewClaim(
            ClaimNumber.Create(policy.Market, year, sequence),
            command.Market,
            command.IncidentDate,
            command.Description,
            Money.Create(command.EstimatedAmount, policy.Currency),
            command.Details);

        var claim = Claim.Submit(context, request, policy).ThrowIfRefused(Guid.Empty, context);
        claims.Add(claim);
        await unitOfWork.SaveAsync(claim, ClaimCommand.Submit, context, cancellationToken);
        return claim;
    }
}
