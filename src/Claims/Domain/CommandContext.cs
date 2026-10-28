namespace Chubb.Claims.Domain;

public sealed record CommandContext(Actor Actor, DateTimeOffset Now, Guid CorrelationId, IIdGenerator Ids);
