using Chubb.Reporting.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Chubb.Reporting.Infrastructure.Persistence.Configurations;

internal sealed class ClaimSnapshotConfiguration : IEntityTypeConfiguration<ClaimSnapshot>
{
    public void Configure(EntityTypeBuilder<ClaimSnapshot> builder)
    {
        builder.ToTable("claim_snapshot");
        builder.HasKey(snapshot => snapshot.ClaimId);
        builder.Property(snapshot => snapshot.ClaimId).HasColumnName("claim_id").ValueGeneratedNever();
        builder.Property(snapshot => snapshot.Number).HasColumnName("number").HasMaxLength(32).IsRequired();
        builder.Property(snapshot => snapshot.Market).HasColumnName("market").HasMaxLength(2).IsRequired();
        builder.Property(snapshot => snapshot.Type).HasColumnName("type").HasMaxLength(16).IsRequired();
        builder.Property(snapshot => snapshot.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(snapshot => snapshot.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(snapshot => snapshot.Reserve).HasColumnName("reserve").HasColumnType("numeric(18,2)");
        builder.Property(snapshot => snapshot.AssigneeId).HasColumnName("assignee_id");
        builder.Property(snapshot => snapshot.AssigneeName).HasColumnName("assignee_name").HasMaxLength(200);
        builder.Property(snapshot => snapshot.SubmittedAt).HasColumnName("submitted_at");
        builder.Property(snapshot => snapshot.LastEventAt).HasColumnName("last_event_at");
        builder.HasIndex(snapshot => new { snapshot.Status, snapshot.Market }).HasDatabaseName("ix_claim_snapshot_status_market");
        builder.HasIndex(snapshot => snapshot.AssigneeId).HasDatabaseName("ix_claim_snapshot_assignee_id");
        builder.HasIndex(snapshot => snapshot.SubmittedAt).HasDatabaseName("ix_claim_snapshot_submitted_at");
    }
}
