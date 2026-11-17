using FluentValidation;
using InviteCraft.Api.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace InviteCraft.Api.Infrastructure;

/// <summary>
/// Runs the FluentValidation validator (if one is registered) for every action argument
/// before the action executes, and short-circuits with a 400 envelope on failure.
/// Replaces the deprecated FluentValidation.AspNetCore auto-validation.
/// </summary>
internal sealed class ValidationFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var services = context.HttpContext.RequestServices;
        var errors = new List<ApiError>();

        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null)
                continue;

            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
            if (services.GetService(validatorType) is not IValidator validator)
                continue;

            var result = await validator.ValidateAsync(
                new ValidationContext<object>(argument),
                context.HttpContext.RequestAborted);

            errors.AddRange(result.Errors.Select(e =>
                new ApiError(e.ErrorCode, e.ErrorMessage, FieldName.ToCamelCase(e.PropertyName))));
        }

        if (errors.Count > 0)
        {
            context.Result = new BadRequestObjectResult(
                ApiResponse.Fail("One or more validation errors occurred.", errors, RequestTrace.For(context.HttpContext)));
            return;
        }

        await next();
    }
}
