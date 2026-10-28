using Chubb.Reporting.Application;
using FluentValidation;

namespace Chubb.Reporting.Api;

public sealed class PageRequestValidator : AbstractValidator<PageRequest>
{
    public PageRequestValidator()
    {
        RuleFor(request => request.Page).GreaterThanOrEqualTo(1);
        RuleFor(request => request.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize);
    }
}
