namespace Chubb.Claims.Domain;

public sealed record PropertyDetails(string PropertyAddress, string DamageType) : ClaimDetails;
