namespace Chubb.Claims.Api;

public sealed record ClaimantClaimPage(IReadOnlyList<ClaimantClaimView> Items, int Page, int PageSize, int TotalCount);
