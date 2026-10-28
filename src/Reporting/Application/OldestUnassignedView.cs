namespace Chubb.Reporting.Application;

public sealed record OldestUnassignedView(Guid ClaimId, string ClaimNumber, string Market, string Type, DateTimeOffset SubmittedAt, double AgeHours);
