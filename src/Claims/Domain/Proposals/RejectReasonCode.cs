namespace Chubb.Claims.Domain;

public enum RejectReasonCode
{
    NotCovered,
    PolicyInactive,
    ExclusionApplies,
    InsufficientEvidence,
    DuplicateClaim,
    NoResponse,
}
