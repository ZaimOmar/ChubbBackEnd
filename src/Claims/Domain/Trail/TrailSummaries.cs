using System.Globalization;

namespace Chubb.Claims.Domain;

public static class TrailSummaries
{
    public static string Received(ClaimNumber number) => $"Claim received: {number}";

    public static string PickedUp() => "Claim picked up for review";

    public static string DecisionUnderReview() => "Decision under review";

    public static string Approved(Money payable) => $"Claim approved for {payable.Currency} {Format(payable)}";

    public static string Rejected(RejectReasonCode code, string claimantReason) =>
        $"Claim rejected: {code.LabelOf()}. {claimantReason}";

    public static string Assessed(Money assessed, Money excess, Money payable, Money reserveBefore, Money reserveAfter) =>
        $"Assessed amount set to {Format(assessed)} (excess {Format(excess)}, payable {Format(payable)}, "
        + $"reserve {Format(reserveBefore)} to {Format(reserveAfter)})";

    public static string Proposed(ProposalOutcome outcome, Money? amount) => amount is { } money
        ? $"Decision proposed: {Label(outcome)} {Format(money)} {money.Currency}"
        : $"Decision proposed: {Label(outcome)}";

    public static string Confirmed(ProposalOutcome outcome, Money? amount) => amount is { } money
        ? $"Decision confirmed: {Label(outcome)} {Format(money)} {money.Currency}"
        : $"Decision confirmed: {Label(outcome)}";

    public static string SentBack() => "Decision sent back to the maker";

    public static string Refused(ClaimCommand command, string refusalType) => $"Refused: {command} ({refusalType})";

    private static string Label(ProposalOutcome outcome) => outcome == ProposalOutcome.Approve ? "approve" : "reject";

    private static string Format(Money money) => money.Amount.ToString("N2", CultureInfo.InvariantCulture);
}
