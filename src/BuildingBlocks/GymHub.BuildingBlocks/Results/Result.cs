namespace GymHub.BuildingBlocks.Results;

public enum ErrorKind
{
    NotFound,
    Forbidden,
    Conflict,
    Invalid,
    PaymentRequired
}

public sealed record Error(ErrorKind Kind, string Message)
{
    public static Error NotFound(string message) => new(ErrorKind.NotFound, message);
    public static Error Forbidden(string message) => new(ErrorKind.Forbidden, message);
    public static Error Conflict(string message) => new(ErrorKind.Conflict, message);
    public static Error Invalid(string message) => new(ErrorKind.Invalid, message);
    public static Error PaymentRequired(string message) => new(ErrorKind.PaymentRequired, message);
}

/// <summary>
/// Outcome of a handler that returns no value.
/// </summary>
public readonly record struct Result(Error? Error)
{
    public bool IsSuccess => Error is null;

    public static Result Success() => new(null);

    public static implicit operator Result(Error error) => new(error);
}

/// <summary>
/// Outcome of a handler: a value or an error. Endpoints turn it into HTTP with <see cref="ResultHttpExtensions"/>.
/// </summary>
public readonly record struct Result<T>(T? Value, Error? Error)
{
    public bool IsSuccess => Error is null;

    public static implicit operator Result<T>(T value) => new(value, null);

    public static implicit operator Result<T>(Error error) => new(default, error);
}
