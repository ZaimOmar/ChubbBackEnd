using Chubb.Contracts;
using Chubb.Contracts.Events;

namespace Chubb.Claims.Domain.Tests;

public sealed class SubmitTests
{
    private readonly Scenario _scenario = new();

    [Fact]
    public void Submit_ValidPolicyOfCaller_CreatesSubmittedClaimWithReserveEqualEstimate()
    {
        var claim = _scenario.Submitted(9_000m);

        claim.Status.Should().Be(ClaimStatus.Submitted);
        claim.AssignedOfficerId.Should().BeNull();
        claim.Reserve.Should().Be(claim.Estimated);
        claim.Estimated.Amount.Should().Be(9_000m);
        claim.Currency.Should().Be("SGD");
        claim.Number.ToString().Should().Be("CLM-SG-2026-000123");
    }

    [Fact]
    public void Submit_Valid_WritesSinglePublicClaimReceivedEntryByClaimant()
    {
        var claim = _scenario.Submitted();

        var entry = claim.DrainPending().Entries.Should().ContainSingle().Subject;

        entry.Visibility.Should().Be(TrailVisibility.Public);
        entry.ActorRole.Should().Be(Roles.Claimant);
        entry.ActorId.Should().Be(_scenario.Claimant.UserId);
        entry.FromStatus.Should().BeNull();
        entry.ToStatus.Should().Be(ClaimStatus.Submitted);
        entry.Summary.Should().Be("Claim received: CLM-SG-2026-000123");
    }

    [Fact]
    public void Submit_Valid_EntryOccurredAtEqualsSubmittedAtAndCarriesReserveAfterOnly()
    {
        var claim = _scenario.Submitted(9_000m);

        var entry = claim.DrainPending().Entries.Single();

        entry.OccurredAt.Should().Be(claim.SubmittedAt);
        entry.ReserveBefore.Should().BeNull();
        entry.ReserveAfter.Should().Be(9_000m);
    }

    [Fact]
    public void Submit_Valid_RaisesClaimSubmittedWithClaimantAndStatus()
    {
        var claim = _scenario.Submitted(9_000m);

        var raised = claim.DrainPending().Events.Should().ContainSingle().Subject;

        raised.Should().Be(new ClaimSubmitted("Submitted", _scenario.Claimant.UserId, "CLM-SG-2026-000123", "Motor", "SGD", 9_000m, 9_000m));
    }

    [Theory]
    [InlineData(Markets.Singapore, "SGD")]
    [InlineData(Markets.HongKong, "HKD")]
    [InlineData(Markets.Malaysia, "MYR")]
    [InlineData(Markets.Thailand, "THB")]
    [InlineData(Markets.Indonesia, "IDR")]
    [InlineData(Markets.Australia, "AUD")]
    public void Submit_EachMarket_UsesMarketCurrency(string market, string currency)
    {
        var policy = Scenario.PolicyFor(_scenario.Claimant.UserId, market, 10_000m, 500m);
        var request = new NewClaim(
            ClaimNumber.Create(market, 2026, 1), market, Scenario.IncidentDate, "Hail damage",
            Money.Create(1_000m, currency), new PropertyDetails("1 High Street", "Hail"));

        var claim = Claim.Submit(_scenario.As(_scenario.Claimant), request, policy).Value;

        claim.Currency.Should().Be(currency);
        claim.Type.Should().Be(ClaimType.Property);
    }

    [Fact]
    public void Submit_Details_ExactlyOneOwnedMemberSet()
    {
        var claim = _scenario.Submitted();

        claim.Type.Should().Be(ClaimType.Motor);
        claim.Details.Should().BeOfType<MotorDetails>();
    }

    [Fact]
    public void Submit_PolicyOfAnotherPerson_PolicyNotFound()
    {
        var result = Claim.Submit(_scenario.As(Scenario.ActorWith(Roles.Claimant)), _scenario.NewClaimRequest(), _scenario.Policy);

        result.ShouldBeRefused(DomainRefusals.Types.PolicyNotFound, trailed: false);
    }

    [Theory]
    [InlineData(2025, 12, 31)]
    [InlineData(2026, 1, 1)]
    public void Submit_IncidentDateBoundaries_PolicyPeriodIsInclusive(int year, int month, int day)
    {
        var request = _scenario.NewClaimRequest() with { IncidentDate = new DateOnly(year, month, day) };

        var result = Claim.Submit(_scenario.As(_scenario.Claimant), request, _scenario.Policy);

        if (year == 2025)
        {
            result.ShouldBeRefused(DomainRefusals.Types.IncidentOutsidePolicyPeriod, trailed: false);
            return;
        }

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Submit_IncidentDateInFuture_IncidentDateInFuture()
    {
        var request = _scenario.NewClaimRequest() with { IncidentDate = new DateOnly(2026, 6, 17) };

        var result = Claim.Submit(_scenario.As(_scenario.Claimant), request, _scenario.Policy);

        result.ShouldBeRefused(DomainRefusals.Types.IncidentDateInFuture, trailed: false);
    }

    [Fact]
    public void Submit_IncidentTodayForClaimantAheadOfUtc_Accepted()
    {
        var now = new DateTimeOffset(2026, 6, 14, 23, 0, 0, TimeSpan.Zero);
        var request = _scenario.NewClaimRequest() with { IncidentDate = new DateOnly(2026, 6, 15) };

        var result = Claim.Submit(_scenario.As(_scenario.Claimant, now), request, _scenario.Policy);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Submit_IncidentBeyondUtcPlusFourteen_Refused()
    {
        var now = new DateTimeOffset(2026, 6, 14, 23, 0, 0, TimeSpan.Zero);
        var request = _scenario.NewClaimRequest() with { IncidentDate = new DateOnly(2026, 6, 16) };

        var result = Claim.Submit(_scenario.As(_scenario.Claimant, now), request, _scenario.Policy);

        result.ShouldBeRefused(DomainRefusals.Types.IncidentDateInFuture, trailed: false);
    }

    [Fact]
    public void Submit_MarketDifferentFromPolicy_MarketMismatch()
    {
        var request = _scenario.NewClaimRequest() with { Market = Markets.HongKong };

        var result = Claim.Submit(_scenario.As(_scenario.Claimant), request, _scenario.Policy);

        result.ShouldBeRefused(DomainRefusals.Types.MarketMismatch, trailed: false);
    }

    [Fact]
    public void Submit_MarketMismatchOnAnotherPersonsPolicy_OwnershipCheckedFirst()
    {
        var request = _scenario.NewClaimRequest() with { Market = Markets.HongKong };

        var result = Claim.Submit(_scenario.As(Scenario.ActorWith(Roles.Claimant)), request, _scenario.Policy);

        result.ShouldBeRefused(DomainRefusals.Types.PolicyNotFound, trailed: false);
    }

    [Fact]
    public void Submit_CurrencyDifferentFromPolicy_CurrencyMismatch()
    {
        var request = _scenario.NewClaimRequest() with { Estimated = Money.Create(100m, "HKD") };

        var result = Claim.Submit(_scenario.As(_scenario.Claimant), request, _scenario.Policy);

        result.ShouldBeRefused(DomainRefusals.Types.CurrencyMismatch, trailed: false);
    }

    [Fact]
    public void Submit_EstimateZero_ValidationError()
    {
        var request = _scenario.NewClaimRequest() with { Estimated = Money.Zero("SGD") };

        var result = Claim.Submit(_scenario.As(_scenario.Claimant), request, _scenario.Policy);

        result.ShouldBeRefused(DomainRefusals.Types.ValidationError, trailed: false);
    }
}
