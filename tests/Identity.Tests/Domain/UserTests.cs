using Chubb.Contracts;
using Chubb.Identity.Domain;

namespace Chubb.Identity.Tests.Domain;

public sealed class UserTests
{
    [Fact]
    public void User_Create_WithNoRoles_IsRejected()
    {
        var result = User.Create(ValidUser([]));

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(UserErrors.NoRoles);
    }

    [Fact]
    public void User_Create_WithClaimantAndOfficer_IsRejected()
    {
        var result = User.Create(ValidUser([Role.Claimant, Role.Officer]));

        result.Error.Should().Be(UserErrors.ClaimantWithStaffRole);
    }

    [Fact]
    public void User_Create_WithOfficerAndManager_Succeeds()
    {
        var result = User.Create(ValidUser([Role.Officer, Role.Manager]));

        result.IsSuccess.Should().BeTrue();
        result.Value.Roles.Should().BeEquivalentTo([Role.Officer, Role.Manager]);
    }

    [Fact]
    public void User_Create_WithDuplicateRoles_IsRejected()
    {
        var result = User.Create(ValidUser([Role.Officer, Role.Officer]));

        result.Error.Should().Be(UserErrors.DuplicateRoles);
    }

    [Theory]
    [InlineData("US")]
    [InlineData("sg")]
    [InlineData("")]
    public void User_Create_WithUnknownMarket_IsRejected(string market)
    {
        var result = User.Create(ValidUser([Role.Officer]) with { Market = market });

        result.Error.Should().Be(UserErrors.UnknownMarket);
    }

    [Fact]
    public void User_Create_WithBlankEmailOrName_IsRejected()
    {
        User.Create(ValidUser([Role.Officer]) with { Email = " " }).Error.Should().Be(UserErrors.EmailRequired);
        User.Create(ValidUser([Role.Officer]) with { DisplayName = "" }).Error.Should().Be(UserErrors.DisplayNameRequired);
    }

    [Fact]
    public void User_Create_NormalizesEmailToLowerCase()
    {
        var result = User.Create(ValidUser([Role.Officer]) with { Email = " Officer@Chubb.Test " });

        result.Value.Email.Should().Be("officer@chubb.test");
    }

    private static NewUser ValidUser(IReadOnlyCollection<Role> roles) => new(
        Guid.NewGuid(), "user@chubb.test", "Test User", Markets.Singapore, "hash", roles, DateTimeOffset.UnixEpoch);
}
