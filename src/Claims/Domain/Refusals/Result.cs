namespace Chubb.Claims.Domain;

public sealed class Result
{
    public static readonly Result Success = new(null);

    private Result(DomainRefusal? refusal) => Refusal = refusal;

    public DomainRefusal? Refusal { get; }

    public bool IsSuccess => Refusal is null;

    public static Result Failure(DomainRefusal refusal) => new(refusal);
}
