using Chubb.Notification.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Chubb.Notification.Infrastructure.Persistence.Configurations;

internal sealed class InboxNotificationConfiguration : IEntityTypeConfiguration<InboxNotification>
{
    public const int MessageMaxLength = 500;

    public void Configure(EntityTypeBuilder<InboxNotification> builder)
    {
        builder.ToTable("notifications", table => table.HasCheckConstraint(
            "ck_notifications_recipient", "(recipient_user_id IS NULL) <> (recipient_role IS NULL)"));
        builder.HasKey(notification => notification.Id);
        builder.Property(notification => notification.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(notification => notification.RecipientUserId).HasColumnName("recipient_user_id");
        builder.Property(notification => notification.RecipientRole).HasColumnName("recipient_role").HasMaxLength(32);
        builder.Property(notification => notification.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(32);
        builder.Property(notification => notification.ClaimId).HasColumnName("claim_id");
        builder.Property(notification => notification.Message).HasColumnName("message").HasMaxLength(MessageMaxLength);
        builder.Property(notification => notification.CreatedAt).HasColumnName("created_at");
        builder.Property(notification => notification.ReadAt).HasColumnName("read_at");
        builder.HasIndex(notification => new { notification.RecipientUserId, notification.CreatedAt })
            .HasDatabaseName("ix_notifications_recipient_user_created")
            .IsDescending(false, true);
        builder.HasIndex(notification => new { notification.RecipientRole, notification.CreatedAt })
            .HasDatabaseName("ix_notifications_recipient_role_created")
            .IsDescending(false, true);
    }
}
