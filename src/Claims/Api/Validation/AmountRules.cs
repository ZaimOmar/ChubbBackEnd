using FluentValidation;

namespace Chubb.Claims.Api;

internal static class AmountRules
{
    public static IRuleBuilderOptions<T, decimal> MustBeMoney<T>(this IRuleBuilder<T, decimal> rule) => rule
        .GreaterThan(0).WithMessage("Must be greater than zero.")
        .LessThanOrEqualTo(RequestLimits.MaxAmount).WithMessage("Is above the allowed maximum.")
        .Must(amount => decimal.Round(amount, 2) == amount).WithMessage("Allows at most 2 decimal places.");

    public static IRuleBuilderOptions<T, string> MustHaveText<T>(this IRuleBuilder<T, string> rule, int maxLength) => rule
        .Must(text => !string.IsNullOrWhiteSpace(text)).WithMessage("Is required.")
        .MaximumLength(maxLength).WithMessage($"Must be at most {maxLength} characters.");

    public static IRuleBuilderOptions<T, string> MustBeReason<T>(this IRuleBuilder<T, string> rule, int maxLength) => rule
        .Must(text => (text?.Trim().Length ?? 0) >= RequestLimits.MinReason)
        .WithMessage($"Must be at least {RequestLimits.MinReason} characters.")
        .MaximumLength(maxLength).WithMessage($"Must be at most {maxLength} characters.");
}
