using Chubb.Claims.Domain;
using Chubb.Contracts;
using FluentValidation;

namespace Chubb.Claims.Api;

public sealed class SubmitClaimRequestValidator : AbstractValidator<SubmitClaimRequest>
{
    public SubmitClaimRequestValidator()
    {
        RuleFor(request => request.PolicyNumber).MustHaveText(RequestLimits.PolicyNumber);
        RuleFor(request => request.Market).Must(Markets.All.Contains).WithMessage("Must be one of the supported markets.");
        RuleFor(request => request.Type).IsInEnum().WithMessage("Must be Motor or Property.");
        RuleFor(request => request.IncidentDate).GreaterThanOrEqualTo(RequestLimits.EarliestIncidentDate)
            .WithMessage("Is too far in the past.");
        RuleFor(request => request.Description).MustHaveText(RequestLimits.Description);
        RuleFor(request => request.EstimatedAmount).MustBeMoney();
        When(request => request.Type == ClaimType.Motor, ValidateMotor);
        When(request => request.Type == ClaimType.Property, ValidateProperty);
    }

    private void ValidateMotor()
    {
        RuleFor(request => request.PropertyDetails).Null().WithMessage("Must be absent for a motor claim.");
        RuleFor(request => request.MotorDetails).NotNull().WithMessage("Is required for a motor claim.")
            .DependentRules(() =>
            {
                RuleFor(request => request.MotorDetails!.VehicleRegistration).MustHaveText(RequestLimits.VehicleRegistration);
                RuleFor(request => request.MotorDetails!.IncidentLocation).MustHaveText(RequestLimits.IncidentLocation);
            });
    }

    private void ValidateProperty()
    {
        RuleFor(request => request.MotorDetails).Null().WithMessage("Must be absent for a property claim.");
        RuleFor(request => request.PropertyDetails).NotNull().WithMessage("Is required for a property claim.")
            .DependentRules(() =>
            {
                RuleFor(request => request.PropertyDetails!.PropertyAddress).MustHaveText(RequestLimits.PropertyAddress);
                RuleFor(request => request.PropertyDetails!.DamageType).MustHaveText(RequestLimits.DamageType);
            });
    }
}
