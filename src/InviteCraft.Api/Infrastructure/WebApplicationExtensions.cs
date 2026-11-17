using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using InviteCraft.Infrastructure.Storage;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;
using InfrastructureDi = InviteCraft.Infrastructure.DependencyInjection;

namespace InviteCraft.Api.Infrastructure;

internal static class WebApplicationExtensions
{
    public static WebApplication UseApiPipeline(this WebApplication app)
    {
        app.UseForwardedHeaders();
        app.UseExceptionHandler();

        // Render terminates TLS at its proxy. Redirecting inside the container breaks the health check.
        if (!app.Environment.IsDevelopment() && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_HTTPS_PORT")))
        {
            app.UseHsts();
            app.UseHttpsRedirection();
        }

        app.UseSerilogRequestLogging(options => options.GetLevel = (context, _, exception) =>
            // A browser that navigates away mid-request is not a server fault (the exception handler answers 499): keep it out of the error log.
            exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested ? LogEventLevel.Information
            : exception is not null || context.Response.StatusCode >= 500 ? LogEventLevel.Error
            : LogEventLevel.Information);

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();                 // /openapi/v1.json
            app.MapScalarApiReference();      // /scalar  (interactive API explorer)
        }

        app.UseMediaFiles();

        app.UseRouting();
        app.UseCors(CorsSettings.PolicyName);
        app.UseAuthentication();   // before the rate limiter so per-user limits can see the user
        app.UseRateLimiter();
        app.UseAuthorization();

        app.MapControllers();
        app.MapHealthEndpoints();

        return app;
    }

    /// <summary>
    /// Serves storage keys under public/ (web images, fonts) at Storage:PublicBaseUrl ("/media").
    /// Every file name contains a unique id, so responses are cached for a year as immutable.
    /// Private originals live outside this folder and are never reachable over HTTP.
    /// </summary>
    private static void UseMediaFiles(this WebApplication app)
    {
        var storage = app.Services.GetRequiredService<IOptions<StorageSettings>>().Value;
        var publicRoot = Path.Combine(storage.ResolveRoot(app.Environment.ContentRootPath), "public");
        Directory.CreateDirectory(publicRoot);

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(publicRoot),
            RequestPath = storage.PublicBaseUrl.TrimEnd('/'),
            ServeUnknownFileTypes = false,
            OnPrepareResponse = context =>
            {
                context.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
                context.Context.Response.Headers.XContentTypeOptions = "nosniff";
                context.Context.Response.Headers.AccessControlAllowOrigin = "*"; // fonts may be loaded cross-origin by the renderer
            },
        });
    }

    private static void MapHealthEndpoints(this WebApplication app)
    {
        // Liveness: process is up. No dependency checks, so a DB outage doesn't restart the container.
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteHealthResponse
        }).DisableRateLimiting();

        // Readiness: dependencies (PostgreSQL) are reachable.
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(InfrastructureDi.ReadinessTag),
            ResponseWriter = WriteHealthResponse
        }).DisableRateLimiting();
    }

    private static Task WriteHealthResponse(HttpContext context, HealthReport report)
    {
        var payload = new
        {
            status = report.Status.ToString(),
            totalDurationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 1),
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                durationMs = Math.Round(entry.Value.Duration.TotalMilliseconds, 1),
                error = entry.Value.Exception?.Message
            })
        };

        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
