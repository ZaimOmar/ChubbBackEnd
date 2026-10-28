using System.Text.Json;
using Chubb.Contracts.Events;
using Chubb.Notification.Domain;

namespace Chubb.Notification.Infrastructure.Messaging;

/// <summary>
/// Which events raise an alert and with what Public text (BR-08, BR-33, BR-64, BR-65). The text never carries
/// the proposed outcome, amount or reason code before the checker has confirmed (only the confirmed event does).
/// </summary>
public static class EventNotificationRules
{
    public static bool IsKnown(EventEnvelope envelope) => envelope.Version == 1 && envelope.Type is
        ClaimAssigned.EventType or DecisionProposed.EventType or ClaimApproved.EventType or ClaimRejected.EventType;

    public static InboxNotification Create(EventEnvelope envelope) => envelope.Type switch
    {
        ClaimAssigned.EventType => Assigned(envelope, ReadPayload<ClaimAssigned>(envelope)),
        ClaimApproved.EventType => Approved(envelope, ReadPayload<ClaimApproved>(envelope)),
        ClaimRejected.EventType => Rejected(envelope, ReadPayload<ClaimRejected>(envelope)),
        _ => InboxNotification.ForManagers(
            NotificationKind.DecisionAwaitsCheck,
            envelope.ClaimId,
            $"Decision proposal awaits your check on {ReadPayload<DecisionProposed>(envelope).ClaimNumber}.",
            envelope.OccurredAt),
    };

    private static InboxNotification Assigned(EventEnvelope envelope, ClaimAssigned payload) =>
        ForClaimant(envelope, payload.ClaimantId, NotificationKind.ClaimAssigned, $"Claim {payload.ClaimNumber} has been picked up for review.");

    private static InboxNotification Rejected(EventEnvelope envelope, ClaimRejected payload) =>
        ForClaimant(envelope, payload.ClaimantId, NotificationKind.ClaimRejected, $"Claim {payload.ClaimNumber} was rejected.");

    private static InboxNotification Approved(EventEnvelope envelope, ClaimApproved payload) =>
        ForClaimant(
            envelope,
            payload.ClaimantId,
            NotificationKind.ClaimApproved,
            FormattableString.Invariant($"Claim {payload.ClaimNumber} was approved for {payload.Currency} {payload.Payable:#,##0.00}."));

    private static InboxNotification ForClaimant(EventEnvelope envelope, Guid claimantId, NotificationKind kind, string message) =>
        InboxNotification.ForClaimant(claimantId, kind, envelope.ClaimId, message, envelope.OccurredAt);

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
