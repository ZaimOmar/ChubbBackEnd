namespace Chubb.Identity.Domain;

public sealed record NewUser(
    Guid Id,
    string Email,
    string DisplayName,
    string Market,
    string PasswordHash,
    IReadOnlyCollection<Role> Roles,
    DateTimeOffset CreatedAt);
