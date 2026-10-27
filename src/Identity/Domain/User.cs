using Chubb.Contracts;

namespace Chubb.Identity.Domain;

public sealed class User
{
    private readonly List<UserRole> _userRoles = [];

    private User()
    {
    }

    public Guid Id { get; private set; }

    public string Email { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public string Market { get; private set; } = string.Empty;

    public string PasswordHash { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyCollection<UserRole> UserRoles => _userRoles;

    public IReadOnlyCollection<Role> Roles => _userRoles.Select(userRole => userRole.Role).ToList();

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public static Result<User> Create(NewUser newUser)
    {
        var failure = Validate(newUser);
        if (failure is not null)
        {
            return Result<User>.Failure(failure);
        }

        var user = new User
        {
            Id = newUser.Id,
            Email = NormalizeEmail(newUser.Email),
            DisplayName = newUser.DisplayName.Trim(),
            Market = newUser.Market,
            PasswordHash = newUser.PasswordHash,
            CreatedAt = newUser.CreatedAt,
        };
        user._userRoles.AddRange(newUser.Roles.Select(role => new UserRole(newUser.Id, role)));
        return Result<User>.Success(user);
    }

    private static DomainError? Validate(NewUser newUser)
    {
        if (string.IsNullOrWhiteSpace(newUser.Email))
        {
            return UserErrors.EmailRequired;
        }

        if (string.IsNullOrWhiteSpace(newUser.DisplayName))
        {
            return UserErrors.DisplayNameRequired;
        }

        if (!Markets.All.Contains(newUser.Market))
        {
            return UserErrors.UnknownMarket;
        }

        return ValidateRoles(newUser.Roles);
    }

    private static DomainError? ValidateRoles(IReadOnlyCollection<Role> roles)
    {
        if (roles.Count == 0)
        {
            return UserErrors.NoRoles;
        }

        if (roles.Distinct().Count() != roles.Count)
        {
            return UserErrors.DuplicateRoles;
        }

        // BR-78: Officer plus Manager is allowed on purpose, a claimant never holds a staff role.
        return roles.Contains(Role.Claimant) && roles.Count > 1 ? UserErrors.ClaimantWithStaffRole : null;
    }
}
