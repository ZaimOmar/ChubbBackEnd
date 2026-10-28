using Chubb.Reporting.Domain;
using Microsoft.EntityFrameworkCore;

namespace Chubb.Reporting.Infrastructure.Persistence;

public sealed class ReportingDbContext(DbContextOptions<ReportingDbContext> options) : DbContext(options)
{
    public const string Schema = "reporting";

    public DbSet<ClaimSnapshot> ClaimSnapshots => Set<ClaimSnapshot>();

    public DbSet<PendingProposal> PendingProposals => Set<PendingProposal>();

    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ReportingDbContext).Assembly);
    }
}
