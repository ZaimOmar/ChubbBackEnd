using Chubb.Contracts;

namespace Chubb.Notification.Application;

/// <summary>Whose notifications a caller may see: a claimant's own (by user id) or the manager alerts (by role).</summary>
public sealed record InboxScope
{
    private InboxScope()
    {
    }

    public Guid? UserId { get; private init; }

    public string? Role { get; private init; }

    public static InboxScope ForClaimant(Guid userId) => new() { UserId = userId };

    public static InboxScope ForManagers() => new() { Role = Roles.Manager };
}
