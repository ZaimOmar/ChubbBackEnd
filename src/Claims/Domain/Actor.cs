using ContractRoles = Chubb.Contracts.Roles;

namespace Chubb.Claims.Domain;

public sealed record Actor(Guid UserId, IReadOnlySet<string> Roles, string? DisplayName = null)
{
    private static readonly string[] RolePrecedence =
        [ContractRoles.Manager, ContractRoles.Officer, ContractRoles.Claimant, ContractRoles.System];

    public bool IsStaff => IsOfficer || IsManager;

    public bool IsOfficer => Roles.Contains(ContractRoles.Officer);

    public bool IsManager => Roles.Contains(ContractRoles.Manager);

    public bool IsClaimant => Roles.Contains(ContractRoles.Claimant);

    public string PrimaryRole => RolePrecedence.FirstOrDefault(Roles.Contains)
        ?? throw new InvalidOperationException("Actor holds no known role.");
}
