using Chubb.Claims.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Chubb.Claims.Infrastructure.Persistence;

internal sealed class ClaimConfiguration : IEntityTypeConfiguration<Claim>
{
    private const string StatusList =
        "'Submitted','UnderReview','InfoRequested','PendingDecisionApproval','Approved','Rejected'," +
        "'PendingSettlementApproval','Settling','Settled'";

    public void Configure(EntityTypeBuilder<Claim> builder)
    {
        ConfigureTable(builder);
        ConfigureColumns(builder);
        ConfigureAmounts(builder);
        ConfigureRelationships(builder);
        ConfigureIndexes(builder);
    }

    private static void ConfigureTable(EntityTypeBuilder<Claim> builder)
    {
        builder.ToTable("claims", table =>
        {
            table.HasCheckConstraint("ck_claims_status", $"status IN ({StatusList})");
            table.HasCheckConstraint("ck_claims_type", "type IN ('Motor','Property')");
            table.HasCheckConstraint("ck_claims_market_currency", MarketCurrencyChecks.Pairs);
            table.HasCheckConstraint(
                "ck_claims_amounts",
                "estimated_amount >= 0 AND reserve >= 0 AND COALESCE(assessed_amount, 0) >= 0 " +
                "AND COALESCE(net_payable, 0) >= 0 AND COALESCE(payable, 0) >= 0 AND COALESCE(paid, 0) >= 0");
            table.HasCheckConstraint("ck_claims_assignment", "(status = 'Submitted') = (assigned_officer_id IS NULL)");
            table.HasCheckConstraint("ck_claims_rejected_reserve", "status <> 'Rejected' OR reserve = 0");
            table.HasCheckConstraint("ck_claims_claim_number", "claim_number ~ '^CLM-[A-Z]{2}-[0-9]{4}-[0-9]{6}$'");
        });
        builder.HasKey(claim => claim.Id);
    }

    private static void ConfigureColumns(EntityTypeBuilder<Claim> builder)
    {
        builder.Property(claim => claim.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(claim => claim.Number).HasColumnName("claim_number").HasMaxLength(20)
            .HasConversion(number => number.ToString(), text => ClaimNumber.Parse(text));
        builder.Property(claim => claim.ClaimantId).HasColumnName("claimant_id");
        builder.Property(claim => claim.Type).HasColumnName("type").HasMaxLength(16).HasConversion<string>();
        builder.Property(claim => claim.Market).HasColumnName("market").HasColumnType("char(2)");
        builder.Property(claim => claim.Currency).HasColumnName("currency").HasColumnType("char(3)");
        builder.Property(claim => claim.Status).HasColumnName("status").HasMaxLength(32).HasConversion<string>();
        builder.Property(claim => claim.AssignedOfficerId).HasColumnName("assigned_officer_id");
        builder.Property(claim => claim.IncidentDate).HasColumnName("incident_date");
        builder.Property(claim => claim.Description).HasColumnName("description");
        builder.Property(claim => claim.SubmittedAt).HasColumnName("submitted_at");
        builder.Property(claim => claim.UpdatedAt).HasColumnName("updated_at");
        builder.Property<uint>("xmin").HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
    }

    // Phase 2 columns are created nullable and stay unmapped by the domain.
    private static void ConfigureAmounts(EntityTypeBuilder<Claim> builder)
    {
        builder.Property<decimal>("_estimated").HasColumnName("estimated_amount").HasPrecision(18, 2);
        builder.Property<decimal?>("_assessed").HasColumnName("assessed_amount").HasPrecision(18, 2);
        builder.Property<decimal?>("_netPayable").HasColumnName("net_payable").HasPrecision(18, 2);
        builder.Property<decimal>("_reserve").HasColumnName("reserve").HasPrecision(18, 2);
        builder.Property<decimal?>("_payable").HasColumnName("payable").HasPrecision(18, 2);
        builder.Property<decimal?>("Paid").HasColumnName("paid").HasPrecision(18, 2);
        builder.Property<bool?>("PayeeOnFile").HasColumnName("payee_on_file");
        builder.Property<string>("PayeeName").HasColumnName("payee_name").HasMaxLength(200);
        builder.Ignore(claim => claim.Estimated);
        builder.Ignore(claim => claim.Assessed);
        builder.Ignore(claim => claim.NetPayable);
        builder.Ignore(claim => claim.Reserve);
        builder.Ignore(claim => claim.Payable);
        builder.Ignore(claim => claim.Details);
    }

    private static void ConfigureRelationships(EntityTypeBuilder<Claim> builder)
    {
        builder.Property<Guid>("PolicyId").HasColumnName("policy_id");
        builder.HasOne(claim => claim.Policy).WithMany().HasForeignKey("PolicyId")
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_claims_policy_id");
        builder.HasMany(claim => claim.Proposals).WithOne().HasForeignKey("ClaimId")
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_proposals_claim_id");
        builder.Navigation(claim => claim.Proposals).HasField("_proposals");
        builder.OwnsOne<MotorDetails>("_motorDetails", owned =>
        {
            owned.ToTable("claim_motor_details");
            owned.WithOwner().HasForeignKey("ClaimId");
            owned.Property<Guid>("ClaimId").HasColumnName("claim_id");
            owned.HasKey("ClaimId");
            owned.Property(details => details.VehicleRegistration).HasColumnName("vehicle_registration").HasMaxLength(20);
            owned.Property(details => details.IncidentLocation).HasColumnName("incident_location").HasMaxLength(200);
        });
        builder.OwnsOne<PropertyDetails>("_propertyDetails", owned =>
        {
            owned.ToTable("claim_property_details");
            owned.WithOwner().HasForeignKey("ClaimId");
            owned.Property<Guid>("ClaimId").HasColumnName("claim_id");
            owned.HasKey("ClaimId");
            owned.Property(details => details.PropertyAddress).HasColumnName("property_address").HasMaxLength(300);
            owned.Property(details => details.DamageType).HasColumnName("damage_type").HasMaxLength(100);
        });
    }

    private static void ConfigureIndexes(EntityTypeBuilder<Claim> builder)
    {
        builder.HasIndex(claim => claim.Number).IsUnique().HasDatabaseName("ux_claims_claim_number");
        builder.HasIndex(claim => new { claim.Status, claim.Market }).HasDatabaseName("ix_claims_status_market");
        builder.HasIndex(claim => claim.AssignedOfficerId).HasDatabaseName("ix_claims_assigned_officer_id");
        builder.HasIndex(claim => claim.SubmittedAt).HasDatabaseName("ix_claims_submitted_at");
        builder.HasIndex(claim => new { claim.ClaimantId, claim.SubmittedAt })
            .HasDatabaseName("ix_claims_claimant_id_submitted_at");
    }
}
