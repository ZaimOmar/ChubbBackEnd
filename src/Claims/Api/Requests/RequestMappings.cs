using Chubb.Claims.Application;
using Chubb.Claims.Domain;

namespace Chubb.Claims.Api;

public static class RequestMappings
{
    // An unknown wire code maps to an undefined enum value, which the domain refuses as reason-code-invalid (422).
    private const RejectReasonCode UnknownReasonCode = (RejectReasonCode)(-1);

    public static SubmitClaimCommand ToCommand(this SubmitClaimRequest request) => new(
        request.PolicyNumber.Trim(),
        request.Market,
        request.IncidentDate,
        request.Description,
        request.EstimatedAmount,
        request.Type == ClaimType.Motor ? request.MotorDetails!.ToDomain() : request.PropertyDetails!.ToDomain());

    public static DecisionProposal ToDomain(this ProposeDecisionRequest request) => new(
        request.Outcome, ParseReasonCode(request.ReasonCode), request.Rationale, request.Justification, request.ClaimantReason);

    private static MotorDetails ToDomain(this MotorDetailsRequest details) =>
        new(details.VehicleRegistration, details.IncidentLocation);

    private static PropertyDetails ToDomain(this PropertyDetailsRequest details) =>
        new(details.PropertyAddress, details.DamageType);

    private static RejectReasonCode? ParseReasonCode(string? wireCode)
    {
        if (wireCode is null)
        {
            return null;
        }

        var known = Enum.GetValues<RejectReasonCode>().Cast<RejectReasonCode?>();
        return known.FirstOrDefault(code => code!.Value.WireCodeOf() == wireCode) ?? UnknownReasonCode;
    }
}
