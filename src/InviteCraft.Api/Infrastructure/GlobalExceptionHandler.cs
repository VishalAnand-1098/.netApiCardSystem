using FluentValidation;
using InviteCraft.Api.Contracts;
using InviteCraft.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace InviteCraft.Api.Infrastructure;

/// <summary>
/// Last line of defence: converts unhandled exceptions into the standard envelope.
/// Expected failures should be returned as <c>Result</c> by services and never reach here.
/// </summary>
internal sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IHostEnvironment environment) : IExceptionHandler
{
    private const int ClientClosedRequest = 499;

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var traceId = RequestTrace.For(httpContext);

        // Client disconnected: nothing useful to send back.
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            logger.LogInformation("Request {Path} was cancelled by the client", httpContext.Request.Path);
            httpContext.Response.StatusCode = ClientClosedRequest;
            return true;
        }

        int status;
        string message;
        List<ApiError>? errors = null;

        switch (exception)
        {
            case ValidationException validation:
                status = StatusCodes.Status400BadRequest;
                message = "One or more validation errors occurred.";
                errors = validation.Errors
                    .Select(e => new ApiError(e.ErrorCode, e.ErrorMessage, FieldName.ToCamelCase(e.PropertyName)))
                    .ToList();
                logger.LogWarning("Validation failed for {Path}: {ErrorCount} error(s)", httpContext.Request.Path, errors.Count);
                break;

            case DomainException domain:
                status = StatusCodes.Status422UnprocessableEntity;
                message = domain.Message;
                errors = [new ApiError(domain.Code, domain.Message)];
                logger.LogWarning("Domain rule violated ({Code}) on {Path}: {Message}", domain.Code, httpContext.Request.Path, domain.Message);
                break;

            // A unique index caught a race the service-level check could not (two admins saving the same slug).
            case DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg }:
                status = StatusCodes.Status409Conflict;
                message = "This value is already in use. Please choose a different one.";
                errors = [new ApiError("Database.UniqueViolation", message, pg.ConstraintName)];
                logger.LogWarning("Unique constraint {Constraint} violated on {Path}", pg.ConstraintName, httpContext.Request.Path);
                break;

            case DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation } pg }:
                status = StatusCodes.Status409Conflict;
                message = "This record is still referenced by other data and cannot be changed this way.";
                errors = [new ApiError("Database.ForeignKeyViolation", message)];
                logger.LogWarning("Foreign key {Constraint} violated on {Path}", pg.ConstraintName, httpContext.Request.Path);
                break;

            case DbUpdateConcurrencyException:
                status = StatusCodes.Status409Conflict;
                message = "This record was changed by someone else. Reload it and try again.";
                errors = [new ApiError("Database.ConcurrencyConflict", message)];
                logger.LogWarning("Concurrency conflict on {Path}", httpContext.Request.Path);
                break;

            case BadHttpRequestException badRequest:
                status = badRequest.StatusCode;
                message = "The request was malformed.";
                logger.LogWarning(badRequest, "Bad request on {Path}", httpContext.Request.Path);
                break;

            default:
                status = StatusCodes.Status500InternalServerError;
                message = "An unexpected error occurred. Please try again later.";
                logger.LogError(exception, "Unhandled exception on {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
                if (environment.IsDevelopment())
                    errors = [new ApiError(exception.GetType().Name, exception.ToString())];
                break;
        }

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(ApiResponse.Fail(message, errors, traceId), cancellationToken);
        return true;
    }
}
