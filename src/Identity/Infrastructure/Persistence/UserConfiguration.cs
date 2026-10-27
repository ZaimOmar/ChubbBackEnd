using Chubb.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Chubb.Identity.Infrastructure.Persistence;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users", table => table.HasCheckConstraint(
            "ck_users_market", "market in ('SG','HK','MY','TH','ID','AU')"));
        builder.HasKey(user => user.Id);
        builder.Property(user => user.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(user => user.Email).HasColumnName("email").HasMaxLength(254).IsRequired();
        builder.Property(user => user.DisplayName).HasColumnName("display_name").HasMaxLength(100).IsRequired();
        builder.Property(user => user.Market).HasColumnName("market").HasColumnType("char(2)").IsRequired();
        builder.Property(user => user.PasswordHash).HasColumnName("password_hash").HasMaxLength(200).IsRequired();
        builder.Property(user => user.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.HasIndex(user => user.Email).IsUnique().HasDatabaseName("ix_users_email");

        builder.Ignore(user => user.Roles);
        builder.HasMany(user => user.UserRoles).WithOne().HasForeignKey(userRole => userRole.UserId);
        builder.Navigation(user => user.UserRoles).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
