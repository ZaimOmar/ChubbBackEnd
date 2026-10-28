namespace Chubb.Claims.Application;

public interface IRefusedAttemptRecorder
{
    /// <summary>Writes the Internal Refused entry for a trailed refusal; reads actor and time from the exception's context.</summary>
    Task RecordAsync(DomainRefusalException refusal, CancellationToken cancellationToken);
}
