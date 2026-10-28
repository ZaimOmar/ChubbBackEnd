using FluentValidation;

namespace Chubb.Claims.Api;

public sealed class SendBackRequestValidator : AbstractValidator<SendBackRequest>
{
    public SendBackRequestValidator() => RuleFor(request => request.Reason).MustBeReason(RequestLimits.Reason);
}
