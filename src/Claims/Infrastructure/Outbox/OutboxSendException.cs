namespace Chubb.Claims.Infrastructure.Outbox;

/// <summary>A send that failed; the message is already sanitised (no payload, no stack) and safe to store and log.</summary>
public sealed class OutboxSendException(bool isRetryable, string sanitisedError, Exception? inner = null)
    : Exception(sanitisedError, inner)
{
    public bool IsRetryable { get; } = isRetryable;
}
