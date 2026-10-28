namespace Chubb.Claims.Domain;

public sealed class Proposal
{
    private readonly decimal? _amount;

    // EF Core materialises the proposal through this constructor and then fills every member from the row.
    private Proposal()
    {
        Rationale = null!;
    }

    internal Proposal(Guid id, DecisionProposal request, Money? amount, Guid makerId, DateTimeOffset proposedAt)
    {
        Id = id;
        Outcome = request.Outcome;
        ReasonCode = request.ReasonCode;
        Rationale = request.Rationale;
        ClaimantReason = request.ClaimantReason?.Trim();
        Justification = request.Justification;
        _amount = amount?.Amount;
        Currency = amount?.Currency;
        MakerId = makerId;
        ProposedAt = proposedAt;
    }

    public Guid Id { get; }

    public ProposalOutcome Outcome { get; }

    public RejectReasonCode? ReasonCode { get; }

    public string Rationale { get; }

    public string? ClaimantReason { get; }

    public string? Justification { get; }

    public string? Currency { get; }

    public Guid MakerId { get; }

    public Guid? CheckerId { get; private set; }

    public ProposalState State { get; private set; } = ProposalState.Open;

    public string? ResolutionReason { get; private set; }

    public DateTimeOffset ProposedAt { get; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    public bool IsOpen => State == ProposalState.Open;

    public Money? Amount => _amount is { } amount && Currency is { } currency ? Money.Create(amount, currency) : null;

    internal void Confirm(Guid checkerId, DateTimeOffset now) => Resolve(ProposalState.Confirmed, checkerId, null, now);

    internal void SendBack(Guid checkerId, string reason, DateTimeOffset now) =>
        Resolve(ProposalState.SentBack, checkerId, reason, now);

    private void Resolve(ProposalState state, Guid checkerId, string? reason, DateTimeOffset now)
    {
        if (!IsOpen)
        {
            throw new InvalidOperationException("A resolved proposal is never reopened.");
        }

        if (checkerId == MakerId)
        {
            throw new InvalidOperationException("The checker of a proposal cannot be its maker.");
        }

        State = state;
        CheckerId = checkerId;
        ResolutionReason = reason;
        ResolvedAt = now;
    }
}
