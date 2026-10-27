using System.Diagnostics.CodeAnalysis;

namespace Chubb.Identity.Domain;

public readonly struct Result<T>
{
    private readonly T? _value;
    private readonly DomainError? _error;

    private Result(T? value, DomainError? error)
    {
        _value = value;
        _error = error;
    }

    [MemberNotNullWhen(true, nameof(_value))]
    [MemberNotNullWhen(false, nameof(_error))]
    public bool IsSuccess => _error is null;

    public T Value => IsSuccess ? _value : throw new InvalidOperationException("A failed result has no value.");

    public DomainError Error => IsSuccess ? throw new InvalidOperationException("A successful result has no error.") : _error;

    public static Result<T> Success(T value) => new(value, null);

    public static Result<T> Failure(DomainError error) => new(default, error);
}
