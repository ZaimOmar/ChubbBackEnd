using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Chubb.Claims.Infrastructure.Persistence;

internal sealed class ClaimNumberSequenceConfiguration : IEntityTypeConfiguration<ClaimNumberSequence>
{
    public void Configure(EntityTypeBuilder<ClaimNumberSequence> builder)
    {
        builder.ToTable("claim_number_sequences", table =>
            table.HasCheckConstraint("ck_claim_number_sequences_last_value", "last_value BETWEEN 1 AND 999999"));
        builder.HasKey(sequence => new { sequence.Market, sequence.Year });
        builder.Property(sequence => sequence.Market).HasColumnName("market").HasColumnType("char(2)");
        builder.Property(sequence => sequence.Year).HasColumnName("year");
        builder.Property(sequence => sequence.LastValue).HasColumnName("last_value");
    }
}
