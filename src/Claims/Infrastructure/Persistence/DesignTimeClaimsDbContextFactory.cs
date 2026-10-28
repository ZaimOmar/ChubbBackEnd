using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Chubb.Claims.Infrastructure.Persistence;

// Used only by `dotnet ef`; it never connects, so the connection string holds no credentials.
internal sealed class DesignTimeClaimsDbContextFactory : IDesignTimeDbContextFactory<ClaimsDbContext>
{
    public ClaimsDbContext CreateDbContext(string[] args) =>
        new(ClaimsDbContextOptions.Build("Host=localhost;Database=claims_design"));
}
