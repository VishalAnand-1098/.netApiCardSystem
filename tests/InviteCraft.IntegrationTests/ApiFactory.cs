using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace InviteCraft.IntegrationTests;

/// <summary>
/// Test host for the API. Tests in this project never open a database connection;
/// admin seeding is disabled so a developer's user-secrets cannot make tests touch their database.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Seed:Admin:Email"] = "",
            ["Seed:Admin:Password"] = "",
            ["Rendering:Enabled"] = "false", // no background workers, so no start-up database access
        }));
}
