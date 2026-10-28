using Chubb.Contracts;
using Chubb.Contracts.Events;

namespace Chubb.Claims.Domain;

public sealed partial class Claim
{
    private const int MinReasonLength = 10;
    private const int MaxNoteLength = 1000;
    private const int MaxClaimantReasonLength = 500;

    private readonly List<Proposal> _proposals = [];
    private readonly List<TrailEntry> _pendingEntries = [];
    private readonly List<IClaimEventPayload> _pendingEvents = [];
    private readonly MotorDetails? _motorDetails;
    private readonly PropertyDetails? _propertyDetails;
    private readonly decimal _estimated;
    private decimal? _assessed;
    private decimal? _netPayable;
    private decimal? _payable;
    private decimal _reserve;

    // EF Core materialises the aggregate through this constructor and then fills every member from the row.
    private Claim()
    {
        Number = null!;
        Policy = null!;
        Market = null!;
        Currency = null!;
        Description = null!;
    }

    private Claim(Guid id, NewClaim request, Policy policy, CommandContext context)
    {
        Id = id;
        Number = request.Number;
        Policy = policy;
        ClaimantId = context.Actor.UserId;
        Market = policy.Market;
        Currency = MarketCurrency.For(policy.Market);
        IncidentDate = request.IncidentDate;
        Description = request.Description;
        _estimated = request.Estimated.Amount;
        _reserve = request.Estimated.Amount;
        Status = ClaimStatus.Submitted;
        SubmittedAt = context.Now;
        UpdatedAt = context.Now;
        _motorDetails = request.Details as MotorDetails;
        _propertyDetails = request.Details as PropertyDetails;
        Type = _motorDetails is not null ? ClaimType.Motor : ClaimType.Property;
        if (_motorDetails is null && _propertyDetails is null)
        {
            throw new ArgumentException("Unsupported claim details.", nameof(request));
        }
    }

    public Guid Id { get; }

    public ClaimNumber Number { get; }

    public Policy Policy { get; }

    public Guid ClaimantId { get; }

    public string Market { get; }

    public string Currency { get; }

    public DateOnly IncidentDate { get; }

    public string Description { get; }

    public ClaimStatus Status { get; private set; }

    public Guid? AssignedOfficerId { get; private set; }

    public DateTimeOffset SubmittedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<Proposal> Proposals => _proposals;

    public ClaimType Type { get; }

    public ClaimDetails Details => _motorDetails is not null
        ? _motorDetails
        : _propertyDetails ?? throw new InvalidOperationException("A claim always has details.");

    public Money Estimated => Money.Create(_estimated, Currency);

    public Money? Assessed => Optional(_assessed);

    public Money? NetPayable => Optional(_netPayable);

    public Money Reserve => Money.Create(_reserve, Currency);

    public Money? Payable => Optional(_payable);

    public static Result<Claim> Submit(CommandContext context, NewClaim request, Policy policy)
    {
        if (CheckSubmission(context, request, policy) is { } refusal)
        {
            return Result<Claim>.Failure(refusal);
        }

        var claim = new Claim(context.Ids.NewId(), request, policy, context);
        claim.RecordSubmission(context);
        return Result<Claim>.Success(claim);
    }

    public Result PickUp(CommandContext context)
    {
        if (!context.Actor.IsOfficer)
        {
            return Result.Failure(DomainRefusals.Forbidden(ClaimCommand.PickUp));
        }

        if (Status != ClaimStatus.Submitted)
        {
            return Result.Failure(DomainRefusals.InvalidTransition(ClaimCommand.PickUp, Status));
        }

        MoveTo(ClaimStatus.UnderReview, context);
        AssignedOfficerId = context.Actor.UserId;
        Record(context, new TrailEntryDraft(TrailEntryType.Assigned, Roles.Officer, TrailSummaries.PickedUp())
        {
            FromStatus = ClaimStatus.Submitted,
            ToStatus = ClaimStatus.UnderReview,
        });
        Raise(new ClaimAssigned(Status.ToString(), ClaimantId, context.Actor.UserId, Number.ToString(), context.Actor.DisplayName));
        return Result.Success;
    }

    public Result Assess(CommandContext context, Money assessed, string note)
    {
        if (CheckAssessment(context, assessed, note) is { } refusal)
        {
            return Result.Failure(refusal);
        }

        var reserveBefore = Reserve;
        var payable = Policy.NetPayableFor(assessed);
        _assessed = assessed.Amount;
        _netPayable = payable.Amount;
        _reserve = payable.Amount;
        UpdatedAt = context.Now;
        RecordAssessment(context, assessed, payable, reserveBefore, note);
        Raise(new ClaimAssessed(
            Status.ToString(), assessed.Amount, Policy.Excess.Amount, payable.Amount, reserveBefore.Amount, payable.Amount, Currency));
        return Result.Success;
    }

    public (IReadOnlyList<TrailEntry> Entries, IReadOnlyList<IClaimEventPayload> Events) DrainPending()
    {
        var drained = (Entries: _pendingEntries.ToArray(), Events: _pendingEvents.ToArray());
        _pendingEntries.Clear();
        _pendingEvents.Clear();
        return drained;
    }

    private static DomainRefusal? CheckSubmission(CommandContext context, NewClaim request, Policy policy)
    {
        if (!context.Actor.IsClaimant)
        {
            return DomainRefusals.Forbidden(ClaimCommand.Submit);
        }

        if (policy.PolicyholderId != context.Actor.UserId)
        {
            return DomainRefusals.PolicyNotFound(ClaimCommand.Submit);
        }

        if (request.Market != policy.Market || request.Number.Market != policy.Market)
        {
            return DomainRefusals.MarketMismatch(ClaimCommand.Submit, policy.Market);
        }

        var currency = MarketCurrency.For(policy.Market);
        if (request.Estimated.Currency != currency)
        {
            return DomainRefusals.CurrencyMismatch(ClaimCommand.Submit, currency);
        }

        if (!request.Estimated.IsPositive || string.IsNullOrWhiteSpace(request.Description))
        {
            return DomainRefusals.ValidationError(ClaimCommand.Submit, "The estimate must be positive and the description is required.");
        }

        return CheckIncidentDate(context, request.IncidentDate, policy);
    }

    // Tolerance of UTC+14 so a claimant ahead of UTC is never refused for today's local date.
    private static DomainRefusal? CheckIncidentDate(CommandContext context, DateOnly incidentDate, Policy policy)
    {
        var latestAcceptable = DateOnly.FromDateTime(context.Now.UtcDateTime.AddHours(14));
        if (incidentDate > latestAcceptable)
        {
            return DomainRefusals.IncidentDateInFuture(ClaimCommand.Submit);
        }

        return policy.Covers(incidentDate) ? null : DomainRefusals.IncidentOutsidePolicyPeriod(ClaimCommand.Submit);
    }

    private DomainRefusal? CheckAssessment(CommandContext context, Money assessed, string note)
    {
        if (RefuseUnlessOfficerIsWorking(ClaimCommand.Assess, context) is { } refusal)
        {
            return refusal;
        }

        if (assessed.Currency != Currency)
        {
            return DomainRefusals.CurrencyMismatch(ClaimCommand.Assess, Currency);
        }

        var isValid = assessed.IsPositive && note.Length <= MaxNoteLength;
        return isValid
            ? null
            : DomainRefusals.ValidationError(ClaimCommand.Assess, "The assessed amount must be positive and the note short.");
    }

    private DomainRefusal? RefuseUnlessOfficerIsWorking(ClaimCommand command, CommandContext context)
    {
        if (!context.Actor.IsOfficer)
        {
            return DomainRefusals.Forbidden(command);
        }

        if (AssignedOfficerId != context.Actor.UserId)
        {
            return DomainRefusals.NotAssignedOfficer(command);
        }

        return Status switch
        {
            ClaimStatus.UnderReview => null,
            ClaimStatus.PendingDecisionApproval => DomainRefusals.ProposalPending(command),
            _ => DomainRefusals.InvalidTransition(command, Status),
        };
    }

    private void RecordSubmission(CommandContext context)
    {
        Record(context, new TrailEntryDraft(TrailEntryType.Submitted, Roles.Claimant, TrailSummaries.Received(Number))
        {
            ToStatus = ClaimStatus.Submitted,
            ReserveAfter = _reserve,
        });
        Raise(new ClaimSubmitted(Status.ToString(), ClaimantId, Number.ToString(), Type.ToString(), Currency, _estimated, _reserve));
    }

    private void RecordAssessment(CommandContext context, Money assessed, Money payable, Money reserveBefore, string note)
    {
        var summary = TrailSummaries.Assessed(assessed, Policy.Excess, payable, reserveBefore, payable);
        Record(context, new TrailEntryDraft(TrailEntryType.Assessed, Roles.Officer, summary)
        {
            Reason = string.IsNullOrWhiteSpace(note) ? null : note,
            ReserveBefore = reserveBefore.Amount,
            ReserveAfter = payable.Amount,
        });
    }

    // The table is authoritative: a command that reaches a missing edge is a programming error, not a user refusal.
    private void MoveTo(ClaimStatus target, CommandContext context)
    {
        if (ClaimStateMachine.Evaluate(Status, target, settlementEnabled: false) != TransitionVerdict.Allowed)
        {
            throw new InvalidOperationException($"No transition from {Status} to {target}.");
        }

        Status = target;
        UpdatedAt = context.Now;
    }

    private Money? Optional(decimal? amount) => amount is { } value ? Money.Create(value, Currency) : null;

    private void Record(CommandContext context, TrailEntryDraft draft) =>
        _pendingEntries.Add(TrailEntry.Create(context, Id, draft));

    private void Raise(IClaimEventPayload payload) => _pendingEvents.Add(payload);
}
