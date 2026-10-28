using Chubb.Claims.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Chubb.Claims.Infrastructure.Persistence;

internal sealed class ProposalConfiguration : IEntityTypeConfiguration<Proposal>
{
    private const string DecisionKind = "Decision";

    public void Configure(EntityTypeBuilder<Proposal> builder)
    {
        builder.ToTable("proposals", table =>
        {
            table.HasCheckConstraint("ck_proposals_kind", "kind IN ('Decision','Settlement')");
            table.HasCheckConstraint("ck_proposals_outcome", "outcome IN ('Approve','Reject')");
            table.HasCheckConstraint("ck_proposals_state", "state IN ('Open','Confirmed','SentBack','Withdrawn')");
            table.HasCheckConstraint("ck_proposals_checker_not_maker", "checker_id IS NULL OR checker_id <> maker_id");
            table.HasCheckConstraint("ck_proposals_approve_amount", "outcome <> 'Approve' OR amount IS NOT NULL");
            table.HasCheckConstraint("ck_proposals_reject_reason", "outcome <> 'Reject' OR reason_code IS NOT NULL");
            table.HasCheckConstraint("ck_proposals_reject_claimant_reason", "outcome <> 'Reject' OR claimant_reason IS NOT NULL");
            table.HasCheckConstraint("ck_proposals_open_unresolved", "(state = 'Open') = (resolved_at IS NULL)");
        });
        builder.HasKey(proposal => proposal.Id);
        builder.Property(proposal => proposal.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property<Guid>("ClaimId").HasColumnName("claim_id");
        builder.Property<string>("Kind").HasColumnName("kind").HasMaxLength(16).IsRequired().HasDefaultValue(DecisionKind);
        builder.Property(proposal => proposal.Outcome).HasColumnName("outcome").HasMaxLength(16).HasConversion<string>();
        builder.Property<decimal?>("_amount").HasColumnName("amount").HasPrecision(18, 2);
        builder.Property(proposal => proposal.Currency).HasColumnName("currency").HasColumnType("char(3)");
        builder.Property(proposal => proposal.ReasonCode).HasColumnName("reason_code").HasMaxLength(32).HasConversion<string>();
        builder.Property(proposal => proposal.Rationale).HasColumnName("rationale");
        builder.Property(proposal => proposal.ClaimantReason).HasColumnName("claimant_reason").HasMaxLength(500);
        builder.Property(proposal => proposal.Justification).HasColumnName("justification");
        builder.Property(proposal => proposal.MakerId).HasColumnName("maker_id");
        builder.Property(proposal => proposal.CheckerId).HasColumnName("checker_id");
        builder.Property(proposal => proposal.State).HasColumnName("state").HasMaxLength(16).HasConversion<string>();
        builder.Property(proposal => proposal.ResolutionReason).HasColumnName("resolution_reason");
        builder.Property(proposal => proposal.ProposedAt).HasColumnName("proposed_at");
        builder.Property(proposal => proposal.ResolvedAt).HasColumnName("resolved_at");
        builder.Ignore(proposal => proposal.Amount);
        builder.Ignore(proposal => proposal.IsOpen);
        builder.HasAlternateKey("ClaimId", nameof(Proposal.Id)).HasName("ux_proposals_claim_id_id");
        builder.HasIndex("ClaimId").IsUnique().HasFilter("state = 'Open'").HasDatabaseName("ux_proposals_one_open_per_claim");
    }
}
