using Chubb.Claims.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Chubb.Claims.Infrastructure.Persistence;

internal sealed class TrailEntryConfiguration : IEntityTypeConfiguration<TrailEntry>
{
    private const string EventTypeList =
        "'Submitted','Assigned','Reassigned','InformationRequested','InformationProvided','Assessed'," +
        "'DecisionProposed','DecisionUnderReview','DecisionConfirmed','DecisionAnnounced','DecisionSentBack'," +
        "'ProposalWithdrawn','Correction','Refused'";

    private const string StatusList =
        "'Submitted','UnderReview','InfoRequested','PendingDecisionApproval','Approved','Rejected'," +
        "'PendingSettlementApproval','Settling','Settled'";

    public void Configure(EntityTypeBuilder<TrailEntry> builder)
    {
        ConfigureTable(builder);
        ConfigureColumns(builder);
        ConfigureRelationships(builder);
    }

    private static void ConfigureTable(EntityTypeBuilder<TrailEntry> builder)
    {
        builder.ToTable("claim_history", table =>
        {
            table.HasCheckConstraint("ck_claim_history_visibility", "visibility IN ('Public','Internal')");
            table.HasCheckConstraint("ck_claim_history_actor_role", "actor_role IN ('Claimant','Officer','Manager','System')");
            table.HasCheckConstraint("ck_claim_history_event_type", $"event_type IN ({EventTypeList})");
            table.HasCheckConstraint("ck_claim_history_from_status", $"from_status IS NULL OR from_status IN ({StatusList})");
            table.HasCheckConstraint("ck_claim_history_to_status", $"to_status IS NULL OR to_status IN ({StatusList})");
            table.HasCheckConstraint("ck_claim_history_summary", "summary <> ''");
            table.HasCheckConstraint(
                "ck_claim_history_status_pair",
                "(from_status IS NULL AND to_status IS NULL) OR (to_status IS NOT NULL AND (from_status IS NOT NULL OR event_type = 'Submitted'))");
            table.HasCheckConstraint(
                "ck_claim_history_public_has_no_staff_data",
                "visibility <> 'Public' OR (maker_id IS NULL AND checker_id IS NULL AND reason IS NULL AND referenced_entry_id IS NULL)");
            table.HasCheckConstraint(
                "ck_claim_history_reserve",
                "(reserve_before IS NULL OR (reserve_before >= 0 AND reserve_after IS NOT NULL)) " +
                "AND (reserve_after IS NULL OR (reserve_after >= 0 AND (reserve_before IS NOT NULL OR event_type = 'Submitted')))");
        });
        builder.HasKey(entry => entry.Id);
    }

    private static void ConfigureColumns(EntityTypeBuilder<TrailEntry> builder)
    {
        builder.Property(entry => entry.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(entry => entry.ClaimId).HasColumnName("claim_id");
        builder.Property(entry => entry.OccurredAt).HasColumnName("occurred_at");
        builder.Property(entry => entry.EventType).HasColumnName("event_type").HasMaxLength(32).HasConversion<string>();
        builder.Property(entry => entry.FromStatus).HasColumnName("from_status").HasMaxLength(32).HasConversion<string>();
        builder.Property(entry => entry.ToStatus).HasColumnName("to_status").HasMaxLength(32).HasConversion<string>();
        builder.Property(entry => entry.ActorId).HasColumnName("actor_id");
        builder.Property(entry => entry.ActorRole).HasColumnName("actor_role").HasMaxLength(16);
        builder.Property(entry => entry.Summary).HasColumnName("summary");
        builder.Property(entry => entry.Reason).HasColumnName("reason");
        builder.Property(entry => entry.Visibility).HasColumnName("visibility").HasMaxLength(16).HasConversion<string>();
        builder.Property(entry => entry.CorrelationId).HasColumnName("correlation_id");
        builder.Property(entry => entry.ProposalId).HasColumnName("proposal_id");
        builder.Property(entry => entry.MakerId).HasColumnName("maker_id");
        builder.Property(entry => entry.CheckerId).HasColumnName("checker_id");
        builder.Property(entry => entry.ReferencedEntryId).HasColumnName("referenced_entry_id");
        builder.Property(entry => entry.ReserveBefore).HasColumnName("reserve_before").HasPrecision(18, 2);
        builder.Property(entry => entry.ReserveAfter).HasColumnName("reserve_after").HasPrecision(18, 2);
    }

    // The composite key makes a link to another claim's proposal impossible (MATCH SIMPLE skips null links).
    // The referenced entry is checked by a trigger instead of a foreign key: the key check locks the referenced
    // row, which needs the UPDATE privilege that the append-only migration revokes.
    private static void ConfigureRelationships(EntityTypeBuilder<TrailEntry> builder)
    {
        builder.HasOne<Claim>().WithMany().HasForeignKey(entry => entry.ClaimId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_claim_history_claim_id");
        builder.HasOne<Proposal>().WithMany()
            .HasForeignKey(entry => new { entry.ClaimId, entry.ProposalId })
            .HasPrincipalKey("ClaimId", nameof(Proposal.Id))
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_claim_history_claim_id_proposal_id");
        builder.HasIndex(entry => new { entry.ClaimId, entry.OccurredAt, entry.Id })
            .HasDatabaseName("ix_claim_history_claim_id_occurred_at_id");
    }
}
