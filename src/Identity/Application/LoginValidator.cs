using FluentValidation;

namespace Chubb.Identity.Application;

public sealed class LoginValidator : AbstractValidator<LoginCommand>
{
    public const int MaxEmailLength = 254;
    public const int MaxPasswordLength = 128;

    public LoginValidator()
    {
        RuleFor(command => command.Email).NotEmpty().MaximumLength(MaxEmailLength).EmailAddress();
        RuleFor(command => command.Password).NotEmpty().MaximumLength(MaxPasswordLength);
    }
}
