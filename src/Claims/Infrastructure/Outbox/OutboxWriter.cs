using System.Text.Json;
using Chubb.Claims.Application;
using Chubb.Claims.Domain;
using Chubb.Claims.Infrastructure.Persistence;
using Chubb.Contracts.Events;
using Microsoft.EntityFrameworkCore;

namespace Chubb.Claims.Infrastructure.Outbox;

public sealed class OutboxWriter(ClaimsDbContext db) : IOutboxWriter
{
    private const string EventTypeConstant = "EventType";
    private const string VersionConstant = "Version";

    public void Add(Guid claimId, string market, IReadOnlyList<IClaimEventPayload> events, CommandContext context, Guid causationId)
    {
        foreach (var payload in events)
        {
            var envelope = Wrap(payload, claimId, market, context, causationId);
            db.Set<OutboxMessage>().Add(new OutboxMessage
            {
                EventId = envelope.EventId,
                ClaimId = claimId,
                Type = envelope.Type,
                Version = envelope.Version,
                Envelope = JsonSerializer.Serialize(envelope, EventJson.Options),
                OccurredAt = envelope.OccurredAt,
            });
        }
    }

    private static EventEnvelope Wrap(
        IClaimEventPayload payload, Guid claimId, string market, CommandContext context, Guid causationId)
    {
        var payloadType = payload.GetType();
        return new EventEnvelope(
            context.Ids.NewId(),
            ReadConstant<string>(payloadType, EventTypeConstant),
            ReadConstant<int>(payloadType, VersionConstant),
            context.Now,
            claimId,
            market,
            context.CorrelationId,
            causationId,
            JsonSerializer.SerializeToElement(payload, payloadType, EventJson.Options));
    }

    // Each payload record declares its own wire type and version as constants (ADR-010).
    private static T ReadConstant<T>(Type payloadType, string name)
    {
        var value = payloadType.GetField(name)?.GetRawConstantValue();
        return value is T constant
            ? constant
            : throw new InvalidOperationException($"{payloadType.Name} declares no {typeof(T).Name} constant {name}.");
    }
}
