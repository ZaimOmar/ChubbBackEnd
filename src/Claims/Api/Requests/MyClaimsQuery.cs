using Chubb.Claims.Domain;

namespace Chubb.Claims.Api;

public sealed record MyClaimsQuery(
    string? Market, ClaimType? Type, ClaimStatus? Status, int Page = 1, int PageSize = 20, string? Sort = null,
    string? ClaimNumber = null, decimal? MinAmount = null, decimal? MaxAmount = null, DateOnly? SubmittedFrom = null, DateOnly? SubmittedTo = null);
