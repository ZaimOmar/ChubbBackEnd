namespace Chubb.Claims.Api;

// Only the code label and the wording the checker confirmed; the internal rationale never reaches the claimant.
public sealed record RejectionReasonView(string Code, string Label, string ClaimantReason);
