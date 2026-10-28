using Chubb.Claims.Domain;
using Microsoft.AspNetCore.WebUtilities;

namespace Chubb.Claims.Api.Problems;

// The single place where errors become RFC 7807 documents; details are fixed texts, ids or codes, never submitted values.
public static class ProblemResults
{
    public const string RetryAfterSeconds = "1";

    public static ProblemDocument FromRefusal(DomainRefusal refusal, string correlationId) =>
        Create(refusal.Type, refusal.Status, refusal.Detail, correlationId);

    public static ProblemDocument ClaimNotFound(Guid claimId, string correlationId) =>
        Create(DomainRefusals.Types.ClaimNotFound, StatusCodes.Status404NotFound, $"Claim {claimId} was not found.", correlationId);

    public static ProblemDocument Validation(IReadOnlyDictionary<string, string[]> errors, string correlationId) =>
        Create(DomainRefusals.Types.ValidationError, StatusCodes.Status400BadRequest, "One or more fields are invalid.", correlationId, errors);

    public static ProblemDocument MalformedBody(string correlationId) =>
        Create(DomainRefusals.Types.ValidationError, StatusCodes.Status400BadRequest, "The request body could not be read.", correlationId);

    public static ProblemDocument IdempotencyKeyMissing(string correlationId) =>
        Create("idempotency-key-missing", StatusCodes.Status400BadRequest, "The Idempotency-Key header is required.", correlationId);

    public static ProblemDocument IdempotencyKeyInvalid(string correlationId) =>
        Create("idempotency-key-invalid", StatusCodes.Status400BadRequest, "The Idempotency-Key header is not valid.", correlationId);

    public static ProblemDocument IdempotencyKeyReused(string correlationId) =>
        Create("idempotency-key-reused", StatusCodes.Status422UnprocessableEntity, "The key was already used with a different request.", correlationId);

    public static ProblemDocument IdempotencyRequestInProgress(string correlationId) =>
        Create("idempotency-request-in-progress", StatusCodes.Status409Conflict, "The same request is still being processed.", correlationId);

    public static ProblemDocument Unauthorized(string correlationId) =>
        Create("unauthorized", StatusCodes.Status401Unauthorized, "A valid access token is required.", correlationId);

    public static ProblemDocument Forbidden(string correlationId) =>
        Create(DomainRefusals.Types.Forbidden, StatusCodes.Status403Forbidden, "The caller's role does not allow this request.", correlationId);

    public static ProblemDocument PayloadTooLarge(string correlationId) =>
        Create("payload-too-large", StatusCodes.Status413PayloadTooLarge, "The request body is too large.", correlationId);

    public static ProblemDocument ServerError(string correlationId) =>
        Create("server-error", StatusCodes.Status500InternalServerError, "An unexpected error occurred.", correlationId);

    private static ProblemDocument Create(
        string type, int status, string detail, string correlationId, IReadOnlyDictionary<string, string[]>? errors = null) =>
        new(type, ReasonPhrases.GetReasonPhrase(status), status, detail, correlationId, errors);
}
