namespace Chubb.Claims.Api;

public sealed record MotorDetailsRequest
{
    public required string VehicleRegistration { get; init; }

    public required string IncidentLocation { get; init; }
}
