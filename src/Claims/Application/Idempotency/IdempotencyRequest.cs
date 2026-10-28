namespace Chubb.Claims.Application;

public sealed record IdempotencyRequest(Guid ActorId, string Route, string Key, byte[] RequestHash);
