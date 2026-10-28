using Chubb.Claims.Domain;

namespace Chubb.Claims.Application;

public sealed class DomainRefusalException(Guid claimId, CommandContext context, DomainRefusal refusal)
    : Exception($"{refusal.Command} refused: {refusal.Type}")
{
    public Guid ClaimId { get; } = claimId;

    public CommandContext Context { get; } = context;

    public DomainRefusal Refusal { get; } = refusal;
}
