namespace Chubb.Claims.Domain;

public sealed class Result<T>
{
    private readonly T _value;

    private Result(T value, DomainRefusal? refusal)
    {
        _value = value;
        Refusal = refusal;
    }

    public DomainRefusal? Refusal { get; }

    public bool IsSuccess => Refusal is null;

    public T Value => IsSuccess
        ? _value
        : throw new InvalidOperationException("A failed result has no value.");

    public static Result<T> Success(T value) => new(value, null);

    // The default value is never exposed: Value throws for a failed result.
    public static Result<T> Failure(DomainRefusal refusal) => new(default!, refusal);
}
