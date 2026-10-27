using Chubb.Identity.Application;

namespace Chubb.Identity.Tests.Application;

public sealed class LoginValidatorTests
{
    private readonly LoginValidator _validator = new();

    [Theory]
    [InlineData("", "password")]
    [InlineData("not-an-email", "password")]
    [InlineData("user@chubb.test", "")]
    public void LoginValidator_Validate_EmptyOrMalformedFields_Fails(string email, string password)
    {
        _validator.Validate(new LoginCommand(email, password)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void LoginValidator_Validate_OverlongFields_Fails()
    {
        var overlongEmail = new string('a', LoginValidator.MaxEmailLength) + "@chubb.test";
        var overlongPassword = new string('p', LoginValidator.MaxPasswordLength + 1);

        _validator.Validate(new LoginCommand(overlongEmail, "password")).IsValid.Should().BeFalse();
        _validator.Validate(new LoginCommand("user@chubb.test", overlongPassword)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void LoginValidator_Validate_ValidCommand_Passes()
    {
        _validator.Validate(new LoginCommand("user@chubb.test", "password")).IsValid.Should().BeTrue();
    }
}
