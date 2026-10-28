using Chubb.Claims.Domain;
using Chubb.Contracts.Events;

namespace Chubb.Claims.Application;

public interface IOutboxWriter
{
    /// <summary>Stages one outbox row per event, in the given order; the caller's SaveChanges commits them.</summary>
    void Add(Guid claimId, string market, IReadOnlyList<IClaimEventPayload> events, CommandContext context, Guid causationId);
}
