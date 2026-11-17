using InviteCraft.Api.Contracts;
using InviteCraft.Api.Infrastructure;
using InviteCraft.Application.Common.Models;
using Microsoft.AspNetCore.Mvc;

namespace InviteCraft.Api.Controllers;

/// <summary>
/// Base for all controllers: translates application <see cref="Result"/> objects into
/// HTTP status codes + the <see cref="ApiResponse{T}"/> envelope. Controllers stay one-liners.
/// </summary>
[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    protected string TraceId => RequestTrace.For(HttpContext);

    protected ActionResult<ApiResponse<T>> OkResponse<T>(T data, string? message = null) =>
        Ok(ApiResponse.Ok(data, message, traceId: TraceId));

    protected ActionResult<ApiResponse<IReadOnlyList<T>>> PagedResponse<T>(PagedResult<T> page) =>
        Ok(ApiResponse.Ok(
            page.Items,
            meta: new PaginationMeta(page.Page, page.PageSize, page.TotalCount, page.TotalPages),
            traceId: TraceId));

    /// <summary>201 Created with a Location header pointing at the new resource.</summary>
    protected ActionResult<ApiResponse<T>> CreatedResult<T>(Result<T> result, Func<T, string> location, string? message = null)
    {
        if (result.IsFailure)
            return ErrorResponse(result.Errors);

        return Created(location(result.Value), ApiResponse.Ok(result.Value, message, traceId: TraceId));
    }

    protected ActionResult<ApiResponse<T>> FromResult<T>(Result<T> result, string? successMessage = null)
    {
        if (result.IsSuccess)
            return OkResponse(result.Value, successMessage);

        return ErrorResponse(result.Errors);
    }

    protected ActionResult<ApiResponse<IReadOnlyList<T>>> FromPagedResult<T>(Result<PagedResult<T>> result)
    {
        if (result.IsSuccess)
            return PagedResponse(result.Value);

        return ErrorResponse(result.Errors);
    }

    protected ActionResult FromResult(Result result, string? successMessage = null)
    {
        if (result.IsSuccess)
            return Ok(ApiResponse.Ok<object?>(null, successMessage, traceId: TraceId));

        return ErrorResponse(result.Errors);
    }

    protected ObjectResult ErrorResponse(IReadOnlyList<Error> errors)
    {
        var first = errors[0];
        var status = first.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status422UnprocessableEntity
        };

        var apiErrors = errors.Select(e => new ApiError(e.Code, e.Message, e.Field)).ToList();
        return StatusCode(status, ApiResponse.Fail(first.Message, apiErrors, TraceId));
    }
}
