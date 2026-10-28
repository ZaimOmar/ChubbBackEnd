using Chubb.Reporting.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Chubb.Reporting.Infrastructure.Persistence.Configurations;

internal sealed class PendingProposalConfiguration : IEntityTypeConfiguration<PendingProposal>
{
    public void Configure(EntityTypeBuilder<PendingProposal> builder)
    {
        builder.ToTable("pending_proposal");
        builder.HasKey(proposal => proposal.ProposalId);
        builder.Property(proposal => proposal.ProposalId).HasColumnName("proposal_id").ValueGeneratedNever();
        builder.Property(proposal => proposal.ClaimId).HasColumnName("claim_id");
        builder.Property(proposal => proposal.MakerId).HasColumnName("maker_id");
        builder.Property(proposal => proposal.Amount).HasColumnName("amount").HasColumnType("numeric(18,2)");
        builder.Property(proposal => proposal.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(proposal => proposal.ProposedAt).HasColumnName("proposed_at");
        builder.HasIndex(proposal => proposal.ClaimId).IsUnique().HasDatabaseName("ux_pending_proposal_claim_id");
        builder.HasIndex(proposal => proposal.ProposedAt).HasDatabaseName("ix_pending_proposal_proposed_at");
        builder.HasOne<ClaimSnapshot>().WithMany().HasForeignKey(proposal => proposal.ClaimId).OnDelete(DeleteBehavior.Cascade);
    }
}
