using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Chubb.Claims.Infrastructure.Idempotency;

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("idempotency_keys", table =>
        {
            table.HasCheckConstraint("ck_idempotency_keys_status_code", "status_code BETWEEN 200 AND 499");
            table.HasCheckConstraint("ck_idempotency_keys_key_length", "length(idempotency_key) BETWEEN 8 AND 128");
        });
        builder.HasKey(record => new { record.ActorId, record.Route, record.Key });
        builder.Property(record => record.ActorId).HasColumnName("actor_id");
        builder.Property(record => record.Route).HasColumnName("route").HasMaxLength(300);
        builder.Property(record => record.Key).HasColumnName("idempotency_key").HasMaxLength(128);
        builder.Property(record => record.RequestHash).HasColumnName("request_hash");
        builder.Property(record => record.StatusCode).HasColumnName("status_code");
        builder.Property(record => record.ResponseBody).HasColumnName("response_body");
        builder.Property(record => record.ContentType).HasColumnName("content_type").HasMaxLength(100);
        builder.Property(record => record.Location).HasColumnName("location").HasMaxLength(300);
        builder.Property(record => record.CreatedAt).HasColumnName("created_at");
        builder.Property(record => record.ExpiresAt).HasColumnName("expires_at");
        builder.HasIndex(record => record.ExpiresAt).HasDatabaseName("ix_idempotency_keys_expires_at");
    }
}
