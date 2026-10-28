using Chubb.Notification.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Chubb.Notification.Infrastructure.Persistence.Configurations;

internal sealed class ProcessedEventConfiguration : IEntityTypeConfiguration<ProcessedEvent>
{
    public void Configure(EntityTypeBuilder<ProcessedEvent> builder)
    {
        builder.ToTable("processed_events");
        builder.HasKey(processed => processed.EventId);
        builder.Property(processed => processed.EventId).HasColumnName("event_id").ValueGeneratedNever();
        builder.Property(processed => processed.ProcessedAt).HasColumnName("processed_at");
    }
}
