namespace InviteCraft.Api.Contracts;

/// <summary>A single error entry. <c>Field</c> is the camelCase JSON path when the error is tied to an input.</summary>
public sealed record ApiError(string Code, string Message, string? Field = null);

public sealed record PaginationMeta(int Page, int PageSize, long TotalCount, int TotalPages);

/// <summary>
/// Uniform response envelope for every JSON endpoint (see docs/ARCHITECTURE.md §4.3).
/// HTTP status codes remain authoritative; the envelope adds a consistent body shape.
/// </summary>
public sealed class ApiResponse<T>
{
    public bool Success { get; init; }
    public T? Data { get; init; }
    public PaginationMeta? Meta { get; init; }
    public string? Message { get; init; }
    public IReadOnlyList<ApiError>? Errors { get; init; }
    public string? TraceId { get; init; }
}

public static class ApiResponse
{
    public static ApiResponse<T> Ok<T>(T data, string? message = null, PaginationMeta? meta = null, string? traceId = null) =>
        new() { Success = true, Data = data, Message = message, Meta = meta, TraceId = traceId };

    public static ApiResponse<object> Fail(string message, IReadOnlyList<ApiError>? errors = null, string? traceId = null) =>
        new() { Success = false, Message = message, Errors = errors, TraceId = traceId };
}
