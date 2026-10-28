namespace Chubb.Claims.Domain;

public static class DomainRefusals
{
    public static class Types
    {
        public const string InvalidTransition = "invalid-transition";
        public const string Phase2NotEnabled = "phase-2-not-enabled";
        public const string ProposalPending = "proposal-pending";
        public const string ProposalNotOpen = "proposal-not-open";
        public const string NotAssignedOfficer = "not-assigned-officer";
        public const string MakerCannotCheck = "maker-cannot-check";
        public const string ClaimAlreadyAssigned = "claim-already-assigned";
        public const string ConcurrencyConflict = "concurrency-conflict";
        public const string PolicyNotFound = "policy-not-found";
        public const string MarketMismatch = "market-mismatch";
        public const string IncidentOutsidePolicyPeriod = "incident-outside-policy-period";
        public const string IncidentDateInFuture = "incident-date-in-future";
        public const string CurrencyMismatch = "currency-mismatch";
        public const string ReasonCodeInvalid = "reason-code-invalid";
        public const string AssessedAmountRequired = "assessed-amount-required";
        public const string PayableZero = "payable-zero";
        public const string JustificationRequired = "justification-required";
        public const string ValidationError = "validation-error";
        public const string Forbidden = "forbidden";
        public const string ClaimNotFound = "claim-not-found";
    }

    private const int NotFound = 404;
    private const int Conflict = 409;
    private const int ForbiddenStatus = 403;
    private const int Unprocessable = 422;
    private const int BadRequest = 400;

    public static DomainRefusal InvalidTransition(ClaimCommand command, ClaimStatus status) =>
        Trailed(command, Types.InvalidTransition, Conflict, $"{command} is not allowed while the claim is {status}.");

    public static DomainRefusal ProposalPending(ClaimCommand command) =>
        Trailed(command, Types.ProposalPending, Conflict, $"{command} is not allowed while a decision proposal is pending.");

    public static DomainRefusal ProposalNotOpen(ClaimCommand command, Guid proposalId) =>
        Trailed(command, Types.ProposalNotOpen, Conflict, $"Proposal {proposalId} is not open on this claim.");

    public static DomainRefusal NotAssignedOfficer(ClaimCommand command) =>
        Trailed(command, Types.NotAssignedOfficer, ForbiddenStatus, $"{command} is reserved for the officer assigned to the claim.");

    public static DomainRefusal MakerCannotCheck(ClaimCommand command) =>
        Trailed(command, Types.MakerCannotCheck, ForbiddenStatus, "The maker of a proposal cannot check it.");

    public static DomainRefusal ClaimAlreadyAssigned(ClaimCommand command) =>
        Trailed(command, Types.ClaimAlreadyAssigned, Conflict, "The claim was taken by another officer.");

    public static DomainRefusal ConcurrencyConflict(ClaimCommand command) =>
        Trailed(command, Types.ConcurrencyConflict, Conflict, "The claim was changed by another request.");

    public static DomainRefusal Forbidden(ClaimCommand command) =>
        Untrailed(command, Types.Forbidden, ForbiddenStatus, $"{command} needs a role the caller does not hold.");

    public static DomainRefusal ClaimNotFound(ClaimCommand command, Guid claimId) =>
        Untrailed(command, Types.ClaimNotFound, NotFound, $"Claim {claimId} was not found.");

    public static DomainRefusal PolicyNotFound(ClaimCommand command) =>
        Untrailed(command, Types.PolicyNotFound, Unprocessable, "No policy with this number belongs to the caller.");

    public static DomainRefusal MarketMismatch(ClaimCommand command, string policyMarket) =>
        Untrailed(command, Types.MarketMismatch, Unprocessable, $"The claim market must be the policy market {policyMarket}.");

    public static DomainRefusal IncidentOutsidePolicyPeriod(ClaimCommand command) =>
        Untrailed(command, Types.IncidentOutsidePolicyPeriod, Unprocessable, "The incident date is outside the policy period.");

    public static DomainRefusal IncidentDateInFuture(ClaimCommand command) =>
        Untrailed(command, Types.IncidentDateInFuture, Unprocessable, "The incident date is in the future.");

    public static DomainRefusal CurrencyMismatch(ClaimCommand command, string expectedCurrency) =>
        Untrailed(command, Types.CurrencyMismatch, Unprocessable, $"Amounts must be in {expectedCurrency}.");

    public static DomainRefusal ReasonCodeInvalid(ClaimCommand command) =>
        Untrailed(command, Types.ReasonCodeInvalid, Unprocessable, "A reject proposal needs one of the listed reason codes.");

    public static DomainRefusal AssessedAmountRequired(ClaimCommand command) =>
        Untrailed(command, Types.AssessedAmountRequired, Unprocessable, "An approve proposal needs an assessed amount.");

    public static DomainRefusal PayableZero(ClaimCommand command) =>
        Untrailed(command, Types.PayableZero, Unprocessable, "The net payable amount is zero, so the claim cannot be approved.");

    public static DomainRefusal JustificationRequired(ClaimCommand command) =>
        Untrailed(command, Types.JustificationRequired, Unprocessable, "A payable above the estimate needs a justification.");

    public static DomainRefusal ValidationError(ClaimCommand command, string detail) =>
        Untrailed(command, Types.ValidationError, BadRequest, detail);

    private static DomainRefusal Trailed(ClaimCommand command, string type, int status, string detail) =>
        new(command, type, status, detail, IsTrailed: true);

    private static DomainRefusal Untrailed(ClaimCommand command, string type, int status, string detail) =>
        new(command, type, status, detail, IsTrailed: false);
}
