using FluentValidation;

namespace Chubb.Claims.Api;

internal static class ColumnFilterRules
{
    private const int MaxClaimNumberLength = 40;

    public static void AddColumnFilterRules<T>(
        this AbstractValidator<T> validator,
        Func<T, string?> claimNumber, Func<T, decimal?> minAmount, Func<T, decimal?> maxAmount,
        Func<T, DateOnly?> from, Func<T, DateOnly?> to)
    {
        validator.RuleFor(query => claimNumber(query)!.Trim().Length).InclusiveBetween(1, MaxClaimNumberLength)
            .When(query => claimNumber(query) is not null).OverridePropertyName("claimNumber")
            .WithMessage($"Must be between 1 and {MaxClaimNumberLength} characters.");
        validator.RuleFor(query => minAmount(query)!.Value).GreaterThanOrEqualTo(0m)
            .When(query => minAmount(query) is not null).OverridePropertyName("minAmount").WithMessage("Must be zero or more.");
        validator.RuleFor(query => maxAmount(query)!.Value).GreaterThanOrEqualTo(0m)
            .When(query => maxAmount(query) is not null).OverridePropertyName("maxAmount").WithMessage("Must be zero or more.");
        validator.RuleFor(query => maxAmount(query)!.Value).GreaterThanOrEqualTo(query => minAmount(query)!.Value)
            .When(query => minAmount(query) is not null && maxAmount(query) is not null)
            .OverridePropertyName("maxAmount").WithMessage("Must not be below minAmount.");
        validator.RuleFor(query => to(query)!.Value).GreaterThanOrEqualTo(query => from(query)!.Value)
            .When(query => from(query) is not null && to(query) is not null)
            .OverridePropertyName("submittedTo").WithMessage("Must not be before submittedFrom.");
    }
}
