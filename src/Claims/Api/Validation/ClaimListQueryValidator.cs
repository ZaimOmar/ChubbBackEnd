using Chubb.Claims.Application;
using Chubb.Contracts;
using FluentValidation;

namespace Chubb.Claims.Api;

public sealed class ClaimListQueryValidator : AbstractValidator<ClaimListQuery>
{
    public ClaimListQueryValidator()
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1).WithMessage("Must be at least 1.");
        RuleFor(query => query.PageSize).InclusiveBetween(1, RequestLimits.MaxPageSize)
            .WithMessage($"Must be between 1 and {RequestLimits.MaxPageSize}.");
        RuleFor(query => query.Sort!).Must(sort => ClaimListSortParser.TryParse(sort, out _)).When(query => query.Sort is not null)
            .WithMessage("Must be oldest or newest.");
        RuleFor(query => query.Market!).Must(Markets.All.Contains).When(query => query.Market is not null)
            .WithMessage("Must be one of the supported markets.");
        RuleFor(query => query.Status!.Value).Must(status => OutstandingClaims.Statuses.Contains(status))
            .When(query => query.Outstanding && query.Status is not null).OverridePropertyName("status")
            .WithMessage("Must be an outstanding status (not Rejected or Settled) when outstanding is true.");
        this.AddColumnFilterRules(
            query => query.ClaimNumber, query => query.MinAmount, query => query.MaxAmount, query => query.SubmittedFrom, query => query.SubmittedTo);
    }
}
