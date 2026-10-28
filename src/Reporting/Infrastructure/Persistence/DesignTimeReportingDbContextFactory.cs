using Microsoft.EntityFrameworkCore.Design;

namespace Chubb.Reporting.Infrastructure.Persistence;

// Used only by `dotnet ef`; it never connects, so the connection string holds no credentials.
internal sealed class DesignTimeReportingDbContextFactory : IDesignTimeDbContextFactory<ReportingDbContext>
{
    public ReportingDbContext CreateDbContext(string[] args) =>
        new(ReportingDbContextOptions.Build("Host=localhost;Database=reporting_design"));
}
