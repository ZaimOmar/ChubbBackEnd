namespace Chubb.Claims.Api;

// Match the column lengths of the claims schema and the caps of the claims-api spec.
internal static class RequestLimits
{
    public const int PolicyNumber = 30;
    public const int Description = 2000;
    public const int VehicleRegistration = 20;
    public const int IncidentLocation = 200;
    public const int PropertyAddress = 300;
    public const int DamageType = 100;
    public const int Note = 1000;
    public const int Rationale = 2000;
    public const int Justification = 2000;
    public const int ClaimantReason = 500;
    public const int Reason = 1000;
    public const int MinReason = 10;
    public const int MaxPageSize = 100;
    public const decimal MaxAmount = 1_000_000_000_000m;
    public static readonly DateOnly EarliestIncidentDate = new(1990, 1, 1);
}
