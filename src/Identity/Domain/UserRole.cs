namespace Chubb.Identity.Domain;

public sealed class UserRole
{
    private UserRole()
    {
    }

    internal UserRole(Guid userId, Role role)
    {
        UserId = userId;
        Role = role;
    }

    public Guid UserId { get; private set; }

    public Role Role { get; private set; }
}
