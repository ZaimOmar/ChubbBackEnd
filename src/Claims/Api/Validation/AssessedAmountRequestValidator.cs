using FluentValidation;

namespace Chubb.Claims.Api;

public sealed class AssessedAmountRequestValidator : AbstractValidator<AssessedAmountRequest>
{
    public AssessedAmountRequestValidator()
    {
        RuleFor(request => request.AssessedAmount).MustBeMoney();
        RuleFor(request => request.Note).MaximumLength(RequestLimits.Note).WithMessage($"Must be at most {RequestLimits.Note} characters.");
    }
}
