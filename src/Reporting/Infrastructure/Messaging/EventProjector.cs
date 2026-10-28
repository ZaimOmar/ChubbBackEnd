using System.Text.Json;
using Chubb.Contracts.Events;
using Chubb.Reporting.Domain;
using Chubb.Reporting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Chubb.Reporting.Infrastructure.Messaging;

public sealed class EventProjector(ReportingDbContext db, TimeProvider clock, ILogger<EventProjector> logger)
{
    public async Task ProjectAsync(string message, CancellationToken cancellationToken)
    {
        var envelope = ReadEnvelope(message);
        if (!IsKnown(envelope))
        {
            logger.LogInformation("Event {EventId} of type {EventType} version {EventVersion} is not projected", envelope.EventId, envelope.Type, envelope.Version);
            return;
        }

        if (await db.ProcessedEvents.AnyAsync(processed => processed.EventId == envelope.EventId, cancellationToken))
        {
            logger.LogInformation("Event {EventId} was already processed", envelope.EventId);
            return;
        }

        await ApplyAsync(envelope, cancellationToken);
        db.ProcessedEvents.Add(ProcessedEvent.Create(envelope.EventId, clock.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
    }

    private static EventEnvelope ReadEnvelope(string message)
    {
        try
        {
            return JsonSerializer.Deserialize<EventEnvelope>(message, EventJson.Options)
                ?? throw new InvalidEventException("The message is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidEventException("The message is not a valid event envelope.", exception);
        }
    }

    private static bool IsKnown(EventEnvelope envelope) => envelope.Version == 1 && envelope.Type is
        ClaimSubmitted.EventType or ClaimAssigned.EventType or ClaimReassigned.EventType or ProposalWithdrawn.EventType or ClaimAssessed.EventType or DecisionProposed.EventType
        or DecisionSentBack.EventType or ClaimApproved.EventType or ClaimRejected.EventType;

    private async Task ApplyAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        if (envelope.Type == ClaimSubmitted.EventType)
        {
            AddSnapshot(envelope);
            return;
        }

        var snapshot = await db.ClaimSnapshots.SingleOrDefaultAsync(claim => claim.ClaimId == envelope.ClaimId, cancellationToken)
            ?? throw new InvalidEventException("The claim has no snapshot yet.");
        if (snapshot.IsOlderThan(envelope.OccurredAt))
        {
            return;
        }

        await ApplyToSnapshotAsync(envelope, snapshot, cancellationToken);
    }

    private void AddSnapshot(EventEnvelope envelope)
    {
        var payload = ReadPayload<ClaimSubmitted>(envelope);
        db.ClaimSnapshots.Add(ClaimSnapshot.Submitted(
            envelope.ClaimId, payload.ClaimNumber, envelope.Market, payload.ClaimType, payload.Currency, payload.Reserve, envelope.OccurredAt));
    }

    private async Task ApplyToSnapshotAsync(EventEnvelope envelope, ClaimSnapshot snapshot, CancellationToken cancellationToken)
    {
        switch (envelope.Type)
        {
            case ClaimAssigned.EventType:
                var assigned = ReadPayload<ClaimAssigned>(envelope);
                snapshot.Assign(assigned.Status, assigned.OfficerId, assigned.AssigneeName, envelope.OccurredAt);
                break;
            case ClaimReassigned.EventType:
                var reassigned = ReadPayload<ClaimReassigned>(envelope);
                snapshot.Assign(reassigned.Status, reassigned.NewOfficerId, null, envelope.OccurredAt);
                break;
            case ProposalWithdrawn.EventType:
                snapshot.MoveTo(ReadPayload<ProposalWithdrawn>(envelope).Status, envelope.OccurredAt);
                await ClearProposalsAsync(snapshot.ClaimId, cancellationToken);
                break;
            case ClaimAssessed.EventType:
                var assessed = ReadPayload<ClaimAssessed>(envelope);
                snapshot.ChangeReserve(assessed.Status, assessed.ReserveAfter, envelope.OccurredAt);
                break;
            case DecisionProposed.EventType:
                await ProposeAsync(envelope, snapshot, cancellationToken);
                break;
            case DecisionSentBack.EventType:
                snapshot.MoveTo(ReadPayload<DecisionSentBack>(envelope).Status, envelope.OccurredAt);
                await ClearProposalsAsync(snapshot.ClaimId, cancellationToken);
                break;
            case ClaimApproved.EventType:
                var approved = ReadPayload<ClaimApproved>(envelope);
                snapshot.ChangeReserve(approved.Status, approved.Payable, envelope.OccurredAt);
                await ClearProposalsAsync(snapshot.ClaimId, cancellationToken);
                break;
            default:
                snapshot.ChangeReserve(ReadPayload<ClaimRejected>(envelope).Status, 0m, envelope.OccurredAt);
                await ClearProposalsAsync(snapshot.ClaimId, cancellationToken);
                break;
        }
    }

    private async Task ProposeAsync(EventEnvelope envelope, ClaimSnapshot snapshot, CancellationToken cancellationToken)
    {
        var payload = ReadPayload<DecisionProposed>(envelope);
        snapshot.MoveTo(payload.Status, envelope.OccurredAt);
        await ClearProposalsAsync(snapshot.ClaimId, cancellationToken);
        db.PendingProposals.Add(PendingProposal.Create(
            payload.ProposalId, snapshot.ClaimId, payload.MakerId, payload.Amount, payload.Currency, envelope.OccurredAt));
    }

    private async Task ClearProposalsAsync(Guid claimId, CancellationToken cancellationToken)
    {
        var open = await db.PendingProposals.Where(proposal => proposal.ClaimId == claimId).ToListAsync(cancellationToken);
        db.PendingProposals.RemoveRange(open);
    }

    private static T ReadPayload<T>(EventEnvelope envelope)
    {
        try
        {
            return envelope.Payload.Deserialize<T>(EventJson.Options)
                ?? throw new InvalidEventException("The event payload is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidEventException("The event payload does not match its type.", exception);
        }
    }
}
