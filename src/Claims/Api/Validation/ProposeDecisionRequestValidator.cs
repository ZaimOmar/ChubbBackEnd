using FluentValidation;

namespace Chubb.Claims.Api;

public sealed class ProposeDecisionRequestValidator : AbstractValidator<ProposeDecisionRequest>
{
    public ProposeDecisionRequestValidator()
    {
        RuleFor(request => request.Outcome).IsInEnum().WithMessage("Must be Approve or Reject.");
        RuleFor(request => request.Rationale).MustBeReason(RequestLimits.Rationale);
        RuleFor(request => request.Justification).MaximumLength(RequestLimits.Justification)
            .WithMessage($"Must be at most {RequestLimits.Justification} characters.");
        RuleFor(request => request.ClaimantReason).MaximumLength(RequestLimits.ClaimantReason)
            .WithMessage($"Must be at most {RequestLimits.ClaimantReason} characters.");
    }
}
