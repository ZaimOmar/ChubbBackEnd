namespace Chubb.Claims.Domain;

public sealed record DomainRefusal(ClaimCommand Command, string Type, int Status, string Detail, bool IsTrailed);
