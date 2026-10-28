namespace Chubb.Reporting.Application;

public sealed record PendingApprovalView(
    Guid ClaimId,
    string ClaimNumber,
    string Market,
    string Type,
    decimal? Amount,
    string Currency,
    Guid MakerId,
    string MakerName,
    DateTimeOffset ProposedAt,
    double AgeHours);
