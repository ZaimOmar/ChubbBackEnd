namespace Chubb.Identity.Domain;

public static class UserErrors
{
    public static readonly DomainError NoRoles = new("user-no-roles", "A user needs at least one role.");

    public static readonly DomainError DuplicateRoles = new("user-duplicate-roles", "A user's roles must be distinct.");

    public static readonly DomainError ClaimantWithStaffRole = new(
        "user-claimant-with-staff-role", "A claimant cannot also hold a staff role.");

    public static readonly DomainError UnknownMarket = new("user-unknown-market", "The market is not supported.");

    public static readonly DomainError EmailRequired = new("user-email-required", "An email is required.");

    public static readonly DomainError DisplayNameRequired = new("user-display-name-required", "A display name is required.");
}
