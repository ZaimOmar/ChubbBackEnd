using System.Reflection;
using Chubb.Contracts;

namespace Chubb.Claims.Domain.Tests;

public sealed class TrailEntryTests
{
    private readonly Scenario _scenario = new();

    [Fact]
    public void TrailEntry_Create_BlankSummary_Throws()
    {
        var create = () => TrailEntry.Create(
            _scenario.As(_scenario.Officer), Guid.NewGuid(), new TrailEntryDraft(TrailEntryType.Assessed, Roles.Officer, " "));

        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TrailEntry_Create_EmptyClaimIdOrUnknownRole_Throws()
    {
        var context = _scenario.As(_scenario.Officer);

        var emptyClaim = () => TrailEntry.Create(context, Guid.Empty, new TrailEntryDraft(TrailEntryType.Assessed, Roles.Officer, "x"));
        var unknownRole = () => TrailEntry.Create(context, Guid.NewGuid(), new TrailEntryDraft(TrailEntryType.Assessed, "Root", "x"));

        emptyClaim.Should().Throw<ArgumentException>();
        unknownRole.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TrailEntry_Create_StatusChangeWithoutBothStatuses_Throws()
    {
        var draft = new TrailEntryDraft(TrailEntryType.Assigned, Roles.Officer, "x") { ToStatus = ClaimStatus.UnderReview };

        var create = () => TrailEntry.Create(_scenario.As(_scenario.Officer), Guid.NewGuid(), draft);

        create.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null, 5.0, TrailEntryType.Assessed)]
    [InlineData(5.0, null, TrailEntryType.Assessed)]
    [InlineData(null, null, TrailEntryType.Submitted)]
    public void TrailEntry_Create_ReserveBeforeWithoutAfterOrMissingBeforeOutsideSubmitted_Throws(
        double? before, double? after, TrailEntryType type)
    {
        var draft = new TrailEntryDraft(type, Roles.Officer, "x")
        {
            ToStatus = type == TrailEntryType.Submitted ? ClaimStatus.Submitted : null,
            ReserveBefore = (decimal?)before,
            ReserveAfter = (decimal?)after,
        };

        var create = () => TrailEntry.Create(_scenario.As(_scenario.Officer), Guid.NewGuid(), draft);

        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TrailEntryType_EveryValue_HasExactlyOneVisibility()
    {
        foreach (var type in Enum.GetValues<TrailEntryType>())
        {
            var visibility = type.VisibilityOf();

            Enum.IsDefined(visibility).Should().BeTrue();
        }
    }

    [Fact]
    public void TrailEntry_PublicType_NeverCarriesMakerCheckerReasonOrReference()
    {
        var context = _scenario.As(_scenario.Officer);
        var drafts = new[]
        {
            new TrailEntryDraft(TrailEntryType.DecisionUnderReview, Roles.Officer, "x") { MakerId = Guid.NewGuid() },
            new TrailEntryDraft(TrailEntryType.DecisionAnnounced, Roles.Manager, "x") { CheckerId = Guid.NewGuid() },
            new TrailEntryDraft(TrailEntryType.DecisionAnnounced, Roles.Manager, "x") { Reason = "internal" },
            new TrailEntryDraft(TrailEntryType.Assigned, Roles.Officer, "x") { ReferencedEntryId = Guid.NewGuid() },
        };

        foreach (var draft in drafts)
        {
            var create = () => TrailEntry.Create(context, Guid.NewGuid(), draft);

            create.Should().Throw<ArgumentException>();
        }
    }

    [Fact]
    public void TrailEntry_Properties_HaveNoPublicSetters()
    {
        var setters = typeof(TrailEntry).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.SetMethod is { IsPublic: true });

        setters.Should().BeEmpty();
    }

    [Fact]
    public void TrailEntryRefused_TrailedRefusal_InternalEntryNamesActorAndAttempt()
    {
        var claimId = Guid.NewGuid();
        var refusal = DomainRefusals.MakerCannotCheck(ClaimCommand.ConfirmDecision);
        var context = _scenario.As(_scenario.DualRole);

        var entry = TrailEntry.Refused(claimId, context, refusal);

        entry.Visibility.Should().Be(TrailVisibility.Internal);
        entry.EventType.Should().Be(TrailEntryType.Refused);
        entry.ActorId.Should().Be(_scenario.DualRole.UserId);
        entry.ActorRole.Should().Be(Roles.Manager);
        entry.Summary.Should().Be("Refused: ConfirmDecision (maker-cannot-check)");
        entry.FromStatus.Should().BeNull();
    }

    [Fact]
    public void TrailEntryRefused_UntrailedRefusal_Throws()
    {
        var refusal = DomainRefusals.PayableZero(ClaimCommand.ProposeDecision);

        var refuse = () => TrailEntry.Refused(Guid.NewGuid(), _scenario.As(_scenario.Officer), refusal);

        refuse.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TrailEntryRefused_Built_DoesNotTouchAnyClaimField()
    {
        var claim = _scenario.Assessed();
        var updatedAt = claim.UpdatedAt;
        var refusal = DomainRefusals.NotAssignedOfficer(ClaimCommand.Assess);

        TrailEntry.Refused(claim.Id, _scenario.As(_scenario.OtherOfficer, Scenario.Now.AddHours(1)), refusal);

        claim.UpdatedAt.Should().Be(updatedAt);
    }

    [Fact]
    public void DomainRefusals_EachTrailedFactory_HasExpectedStatus()
    {
        var command = ClaimCommand.ConfirmDecision;
        var expected = new (DomainRefusal Refusal, int Status, bool Trailed)[]
        {
            (DomainRefusals.InvalidTransition(command, ClaimStatus.Approved), 409, true),
            (DomainRefusals.ProposalPending(command), 409, true),
            (DomainRefusals.ProposalNotOpen(command, Guid.NewGuid()), 409, true),
            (DomainRefusals.NotAssignedOfficer(command), 403, true),
            (DomainRefusals.MakerCannotCheck(command), 403, true),
            (DomainRefusals.ClaimAlreadyAssigned(command), 409, true),
            (DomainRefusals.ConcurrencyConflict(command), 409, true),
            (DomainRefusals.PolicyNotFound(command), 422, false),
            (DomainRefusals.MarketMismatch(command, "SG"), 422, false),
            (DomainRefusals.IncidentOutsidePolicyPeriod(command), 422, false),
            (DomainRefusals.IncidentDateInFuture(command), 422, false),
            (DomainRefusals.CurrencyMismatch(command, "SGD"), 422, false),
            (DomainRefusals.ReasonCodeInvalid(command), 422, false),
            (DomainRefusals.AssessedAmountRequired(command), 422, false),
            (DomainRefusals.PayableZero(command), 422, false),
            (DomainRefusals.JustificationRequired(command), 422, false),
            (DomainRefusals.ValidationError(command, "x"), 400, false),
        };

        foreach (var (refusal, status, trailed) in expected)
        {
            refusal.Status.Should().Be(status, refusal.Type);
            refusal.IsTrailed.Should().Be(trailed, refusal.Type);
        }

        expected.Select(item => item.Refusal.Type).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Commands_DomainSources_UseContextClockAndIdGenerator()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "global.json")))
        {
            root = root.Parent;
        }

        var sources = Directory.GetFiles(Path.Combine(root?.FullName ?? string.Empty, "src", "Claims", "Domain"), "*.cs", SearchOption.AllDirectories);

        sources.Should().NotBeEmpty();
        foreach (var source in sources)
        {
            var text = File.ReadAllText(source);
            text.Should().NotContain("Guid.NewGuid").And.NotContain("DateTime.UtcNow").And.NotContain("DateTimeOffset.UtcNow", source);
        }
    }
}
