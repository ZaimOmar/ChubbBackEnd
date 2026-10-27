using Chubb.Identity.Application;
using Chubb.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace Chubb.Identity.Infrastructure.Persistence;

public sealed class UserRepository(IdentityDbContext db) : IUserRepository
{
    public Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        db.Users
            .AsNoTracking()
            .Include(user => user.UserRoles)
            .SingleOrDefaultAsync(user => user.Email == normalizedEmail, cancellationToken);
}
