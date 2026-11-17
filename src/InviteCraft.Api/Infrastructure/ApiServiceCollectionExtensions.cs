using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using InviteCraft.Api.Contracts;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;

namespace InviteCraft.Api.Infrastructure;

internal static class ApiServiceCollectionExtensions
{
    public static IServiceCollection AddApiServices(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services
            .AddControllers(options =>
            {
                options.Filters.Add<ValidationFilter>();
                // FluentValidation owns validation; don't treat non-nullable reference types as [Required].
                options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
            })
            .AddJsonOptions(options =>
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .ConfigureApiBehaviorOptions(options =>
            {
                // Model-binding failures (malformed JSON, wrong types) use the same envelope as validation errors.
                options.InvalidModelStateResponseFactory = context =>
                {
                    var errors = context.ModelState
                        .Where(entry => entry.Value is { Errors.Count: > 0 })
                        .SelectMany(entry => entry.Value!.Errors.Select(error => new ApiError(
                            "InvalidValue",
                            string.IsNullOrWhiteSpace(error.ErrorMessage) ? "The value is invalid." : error.ErrorMessage,
                            FieldName.ToCamelCase(entry.Key))))
                        .ToList();

                    return new BadRequestObjectResult(ApiResponse.Fail(
                        "One or more validation errors occurred.", errors, RequestTrace.For(context.HttpContext)));
                };
            });

        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddOpenApi();
        services.AddApiAuthentication(configuration, environment);

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            // The platform proxy (Render, nginx) terminates TLS. The container only sees that proxy.
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();
        });

        services.AddSpaCors(configuration);
        services.AddApiRateLimiting(configuration);

        return services;
    }

    private static void AddSpaCors(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(CorsSettings.SectionName).Get<CorsSettings>() ?? new CorsSettings();

        services.AddCors(options => options.AddPolicy(CorsSettings.PolicyName, policy =>
        {
            if (settings.AllowedOrigins.Length == 0)
                return; // No cross-origin access unless explicitly configured.

            policy.WithOrigins(settings.AllowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials() // refresh-token cookie
                .WithExposedHeaders("Content-Disposition");
        }));
    }

    private static void AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(RateLimitingSettings.SectionName).Get<RateLimitingSettings>()
                       ?? new RateLimitingSettings();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();

                await context.HttpContext.Response.WriteAsJsonAsync(
                    ApiResponse.Fail("Too many requests. Please slow down and try again shortly.",
                        traceId: RequestTrace.For(context.HttpContext)),
                    cancellationToken);
            };

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                FixedWindowPerMinute(ClientIp(context), settings.GlobalPermitPerMinute));

            options.AddPolicy(RateLimitPolicies.Auth, context =>
                FixedWindowPerMinute(ClientIp(context), settings.AuthPermitPerMinute));

            options.AddPolicy(RateLimitPolicies.Uploads, context =>
                FixedWindowPerMinute(context.User.FindFirst("sub")?.Value ?? ClientIp(context), settings.UploadPermitPerMinute));

            options.AddPolicy(RateLimitPolicies.Renders, context =>
                FixedWindowPerMinute(context.User.FindFirst("sub")?.Value ?? ClientIp(context), settings.RenderPermitPerMinute));

            options.AddPolicy(RateLimitPolicies.Payments, context =>
                FixedWindowPerMinute(context.User.FindFirst("sub")?.Value ?? ClientIp(context), settings.PaymentPermitPerMinute));
        });
    }

    private static RateLimitPartition<string> FixedWindowPerMinute(string key, int permitLimit) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        });

    private static string ClientIp(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
