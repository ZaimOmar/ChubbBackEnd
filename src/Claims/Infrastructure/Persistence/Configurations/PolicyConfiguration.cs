using Chubb.Claims.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Chubb.Claims.Infrastructure.Persistence;

internal sealed class PolicyConfiguration : IEntityTypeConfiguration<Policy>
{
    public void Configure(EntityTypeBuilder<Policy> builder)
    {
        builder.ToTable("policies", table =>
        {
            table.HasCheckConstraint("ck_policies_market", MarketCurrencyChecks.Markets);
            table.HasCheckConstraint("ck_policies_market_currency", MarketCurrencyChecks.Pairs);
            table.HasCheckConstraint("ck_policies_amounts", "coverage_limit >= 0 AND excess >= 0");
            table.HasCheckConstraint("ck_policies_period", "valid_from <= valid_to");
        });
        builder.HasKey(policy => policy.Id);
        builder.Property(policy => policy.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(policy => policy.Number).HasColumnName("policy_number").HasMaxLength(30).IsRequired();
        builder.Property(policy => policy.PolicyholderId).HasColumnName("policyholder_id");
        builder.Property(policy => policy.Market).HasColumnName("market").HasColumnType("char(2)").IsRequired();
        builder.Property(policy => policy.Currency).HasColumnName("currency").HasColumnType("char(3)").IsRequired();
        builder.Property(policy => policy.PeriodStart).HasColumnName("valid_from");
        builder.Property(policy => policy.PeriodEnd).HasColumnName("valid_to");
        builder.Property<decimal>("_coverageLimit").HasColumnName("coverage_limit").HasPrecision(18, 2);
        builder.Property<decimal>("_excess").HasColumnName("excess").HasPrecision(18, 2);
        builder.Ignore(policy => policy.Excess);
        builder.Ignore(policy => policy.CoverageLimit);
        builder.HasIndex(policy => policy.Number).IsUnique().HasDatabaseName("ux_policies_policy_number");
    }
}
