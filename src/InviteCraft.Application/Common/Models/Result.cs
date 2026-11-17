namespace InviteCraft.Application.Common.Models;

/// <summary>
/// Outcome of an application operation. Services return <see cref="Result"/> / <see cref="Result{T}"/>
/// for expected failures (not found, conflict, business rule) and throw only for unexpected ones.
/// </summary>
public class Result
{
    protected Result(bool isSuccess, IReadOnlyList<Error> errors)
    {
        if (isSuccess && errors.Count > 0)
            throw new ArgumentException("A successful result cannot carry errors.", nameof(errors));
        if (!isSuccess && errors.Count == 0)
            throw new ArgumentException("A failed result must carry at least one error.", nameof(errors));

        IsSuccess = isSuccess;
        Errors = errors;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public IReadOnlyList<Error> Errors { get; }
    public Error? FirstError => Errors.Count > 0 ? Errors[0] : null;

    public static implicit operator Result(Error error) => Failure(error);

    public static Result Success() => new(true, []);
    public static Result Failure(Error error) => new(false, [error]);
    public static Result Failure(IEnumerable<Error> errors) => new(false, errors.ToList());

    public static Result<T> Success<T>(T value) => new(value);
    public static Result<T> Failure<T>(Error error) => new([error]);
    public static Result<T> Failure<T>(IEnumerable<Error> errors) => new(errors.ToList());
}

public sealed class Result<T> : Result
{
    private readonly T? _value;

    internal Result(T value) : base(true, []) => _value = value;

    internal Result(IReadOnlyList<Error> errors) : base(false, errors) { }

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot access the value of a failed result.");

    public static implicit operator Result<T>(T value) => new(value);
    public static implicit operator Result<T>(Error error) => new([error]);
}
