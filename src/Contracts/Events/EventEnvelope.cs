using System.Text.Json;

namespace Chubb.Contracts.Events;

public sealed record EventEnvelope(
    Guid EventId,
    string Type,
    int Version,
    DateTimeOffset OccurredAt,
    Guid ClaimId,
    string Market,
    Guid CorrelationId,
    Guid CausationId,
    JsonElement Payload);
