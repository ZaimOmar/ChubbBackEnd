namespace Chubb.Claims.Application;

public sealed record OutboxRecord(Guid EventId, long Sequence, Guid ClaimId, string Type, int Version, string Envelope, int Attempts);
