using System.Security.Claims;
using Chubb.Claims.Domain;
using Chubb.Contracts;

namespace Chubb.Claims.Api;

public static class ActorFactory
{
    /// <summary>Builds the actor from the validated token only; a missing or non-GUID subject gives null.</summary>
    public static Actor? TryCreate(ClaimsPrincipal user)
    {
        if (!Guid.TryParse(user.FindFirstValue(ClaimNames.Subject), out var userId))
        {
            return null;
        }

        var roles = user.FindAll(ClaimNames.Role).Select(claim => claim.Value).ToHashSet();
        return new Actor(userId, roles, user.FindFirstValue(ClaimNames.Name));
    }
}
