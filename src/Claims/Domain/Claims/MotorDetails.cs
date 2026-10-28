namespace Chubb.Claims.Domain;

public sealed record MotorDetails(string VehicleRegistration, string IncidentLocation) : ClaimDetails;
