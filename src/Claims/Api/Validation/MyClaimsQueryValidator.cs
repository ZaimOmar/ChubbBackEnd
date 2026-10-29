using Chubb.Contracts;
using FluentValidation;

namespace Chubb.Claims.Api;

public sealed class MyClaimsQueryValidator : AbstractValidator<MyClaimsQuery>
{
    public MyClaimsQueryValidator()
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1).WithMessage("Must be at least 1.");
        RuleFor(query => query.PageSize).InclusiveBetween(1, RequestLimits.MaxPageSize)
            .WithMessage($"Must be between 1 and {RequestLimits.MaxPageSize}.");
        RuleFor(query => query.Sort!).Must(sort => ClaimListSortParser.TryParse(sort, out _)).When(query => query.Sort is not null)
            .WithMessage("Must be oldest or newest.");
        RuleFor(query => query.Market!).Must(Markets.All.Contains).When(query => query.Market is not null)
            .WithMessage("Must be one of the supported markets.");
        this.AddColumnFilterRules(
            query => query.ClaimNumber, query => query.MinAmount, query => query.MaxAmount, query => query.SubmittedFrom, query => query.SubmittedTo);
    }
}
