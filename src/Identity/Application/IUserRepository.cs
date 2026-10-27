using Chubb.Identity.Domain;

namespace Chubb.Identity.Application;

public interface IUserRepository
{
    Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken);
}
