using Chubb.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Chubb.Identity.Infrastructure.Persistence;

public sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("user_roles", table => table.HasCheckConstraint(
            "ck_user_roles_role", "role in ('Claimant','Officer','Manager')"));
        builder.HasKey(userRole => new { userRole.UserId, userRole.Role });
        builder.Property(userRole => userRole.UserId).HasColumnName("user_id");
        builder.Property(userRole => userRole.Role).HasColumnName("role").HasConversion<string>().HasMaxLength(20);
    }
}
