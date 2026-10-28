using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Chubb.Claims.Infrastructure.Outbox;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public const int LastErrorMaxLength = 1000;

    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox", table =>
        {
            table.HasCheckConstraint("ck_outbox_attempts", "attempts >= 0");
            table.HasCheckConstraint("ck_outbox_last_error", $"last_error IS NULL OR length(last_error) <= {LastErrorMaxLength}");
        });
        builder.HasKey(message => message.EventId);
        builder.Property(message => message.EventId).HasColumnName("event_id").ValueGeneratedNever();
        builder.Property(message => message.Sequence).HasColumnName("sequence").UseIdentityAlwaysColumn();
        builder.Property(message => message.ClaimId).HasColumnName("claim_id");
        builder.Property(message => message.Type).HasColumnName("type").HasMaxLength(64);
        builder.Property(message => message.Version).HasColumnName("version");
        builder.Property(message => message.Envelope).HasColumnName("envelope").HasColumnType("jsonb");
        builder.Property(message => message.OccurredAt).HasColumnName("occurred_at");
        builder.Property(message => message.PublishedAt).HasColumnName("published_at");
        builder.Property(message => message.Attempts).HasColumnName("attempts");
        builder.Property(message => message.LastError).HasColumnName("last_error");
        builder.HasAlternateKey(message => message.Sequence).HasName("ux_outbox_sequence");
        builder.HasIndex(message => message.Sequence).HasFilter("published_at IS NULL")
            .HasDatabaseName("ix_outbox_unpublished_sequence");
    }
}
