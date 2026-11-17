namespace InviteCraft.Application.Common.Models;

public enum ErrorType
{
    Failure,
    Validation,
    NotFound,
    Conflict,
    Unauthorized,
    Forbidden
}

/// <summary>
/// An expected, non-exceptional failure returned by an application service.
/// <paramref name="Code"/> is a stable machine-readable identifier (e.g. "Template.NotFound")
/// that the Angular client can switch on.
/// </summary>
public sealed record Error(string Code, string Message, ErrorType Type = ErrorType.Failure, string? Field = null)
{
    public static Error Failure(string code, string message) => new(code, message);
    public static Error Validation(string code, string message, string? field = null) => new(code, message, ErrorType.Validation, field);
    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);
    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);
    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);
    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);
}
