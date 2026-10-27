using Chubb.Contracts;
using Chubb.Identity.Domain;
using Chubb.Identity.Infrastructure;
using Chubb.TestSupport;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Chubb.Identity.Tests.Infrastructure;

public sealed class TokenIssuerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);

    private readonly TestKeyPair _keys = new();
    private readonly TokenIssuer _issuer;

    public TokenIssuerTests()
    {
        var options = Options.Create(new JwtIssuerOptions
        {
            Issuer = TestTokens.Issuer,
            Audience = TestTokens.Audience,
            LifetimeMinutes = 30,
            PrivateKeyPath = _keys.PrivateKeyPath,
        });
        _issuer = new TokenIssuer(options, _keys.SigningKey, new FakeTimeProvider(Now));
    }

    public void Dispose() => _keys.Dispose();

    [Fact]
    public void TokenIssuer_Issue_ContainsExpectedClaimsAndThirtyMinuteExpiry()
    {
        var user = CreateUser(Role.Officer);

        var token = _issuer.Issue(user);
        var jwt = new JsonWebToken(token.Value);

        token.ExpiresInSeconds.Should().Be(1800);
        jwt.Subject.Should().Be(user.Id.ToString());
        jwt.GetPayloadValue<string>(ClaimNames.Name).Should().Be("Test User");
        jwt.GetPayloadValue<string>(ClaimNames.Market).Should().Be("SG");
        jwt.Issuer.Should().Be(TestTokens.Issuer);
        jwt.Audiences.Should().ContainSingle().Which.Should().Be(TestTokens.Audience);
        jwt.IssuedAt.Should().Be(Now.UtcDateTime);
        jwt.ValidTo.Should().Be(Now.UtcDateTime.AddMinutes(30));
        jwt.Id.Should().NotBeNullOrEmpty();
        jwt.GetPayloadValue<string[]>(ClaimNames.Role).Should().Equal("Officer");
    }

    [Fact]
    public void TokenIssuer_Issue_ForDualRoleUser_HasBothRoleClaims()
    {
        var token = _issuer.Issue(CreateUser(Role.Officer, Role.Manager));

        new JsonWebToken(token.Value).GetPayloadValue<string[]>(ClaimNames.Role)
            .Should().BeEquivalentTo(Roles.Officer, Roles.Manager);
    }

    [Fact]
    public async Task TokenIssuer_Issue_IsVerifiableWithPublicKeyOnly()
    {
        var token = _issuer.Issue(CreateUser(Role.Manager));
        var publicOnly = new RsaSecurityKey(System.Security.Cryptography.RSA.Create());
        publicOnly.Rsa.ImportFromPem(_keys.PublicKeyPem);

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token.Value, new TokenValidationParameters
        {
            ValidIssuer = TestTokens.Issuer,
            ValidAudience = TestTokens.Audience,
            IssuerSigningKey = publicOnly,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ValidateLifetime = false,
        });

        result.IsValid.Should().BeTrue();
        result.ClaimsIdentity.Claims.Should().Contain(claim => claim.Type == ClaimNames.Role && claim.Value == Roles.Manager);
    }

    private static User CreateUser(params Role[] roles) => User.Create(new NewUser(
        Guid.NewGuid(), "user@chubb.test", "Test User", Markets.Singapore, "hash", roles, Now)).Value;
}
