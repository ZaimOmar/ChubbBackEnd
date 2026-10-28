namespace Chubb.Claims.Api;

public sealed record StaffClaimPage(IReadOnlyList<StaffClaimView> Items, int Page, int PageSize, int TotalCount);
