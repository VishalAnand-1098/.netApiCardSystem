using InviteCraft.Api.Infrastructure;
using InviteCraft.Application;
using InviteCraft.Infrastructure;
using InviteCraft.Infrastructure.Persistence.Seed;
using Serilog;

// Console logger for startup; replaced by the configured logger once the host is built.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddSerilog((services, logger) => logger
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    builder.Services
        .AddApplication()
        .AddInfrastructure(builder.Configuration)
        .AddApiServices(builder.Configuration, builder.Environment);

    var app = builder.Build();

    app.UseApiPipeline();

    // Creates the first administrator from Seed:Admin:* settings (user-secrets), if none exists yet.
    await app.Services.SeedAdminAsync();

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "InviteCraft API terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

// Exposed for WebApplicationFactory<Program> in integration tests.
public partial class Program;
