namespace Chubb.Claims.Domain;

public static class RejectReasonCodeLabels
{
    public static string LabelOf(this RejectReasonCode code) => code switch
    {
        RejectReasonCode.NotCovered => "Not covered",
        RejectReasonCode.PolicyInactive => "Policy inactive",
        RejectReasonCode.ExclusionApplies => "Exclusion applies",
        RejectReasonCode.InsufficientEvidence => "Insufficient evidence",
        RejectReasonCode.DuplicateClaim => "Duplicate claim",
        RejectReasonCode.NoResponse => "No response",
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown reject reason code."),
    };

    public static string WireCodeOf(this RejectReasonCode code) => code switch
    {
        RejectReasonCode.NotCovered => "NOT_COVERED",
        RejectReasonCode.PolicyInactive => "POLICY_INACTIVE",
        RejectReasonCode.ExclusionApplies => "EXCLUSION_APPLIES",
        RejectReasonCode.InsufficientEvidence => "INSUFFICIENT_EVIDENCE",
        RejectReasonCode.DuplicateClaim => "DUPLICATE_CLAIM",
        RejectReasonCode.NoResponse => "NO_RESPONSE",
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown reject reason code."),
    };
}
