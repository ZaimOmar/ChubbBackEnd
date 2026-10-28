using Chubb.Claims.Domain;

namespace Chubb.Claims.Api;

public sealed class CommandContextFactory(TimeProvider clock, IIdGenerator ids)
{
    public CommandContext Create(HttpContext http)
    {
        var actor = ActorFactory.TryCreate(http.User) ?? throw new UnauthorizedAccessException();
        var correlationId = Guid.Parse(CorrelationIdMiddleware.From(http));
        return new CommandContext(actor, clock.GetUtcNow(), correlationId, ids);
    }
}
