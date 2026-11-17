using System.Reflection;
using InviteCraft.Api.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace InviteCraft.Api.Controllers;

public sealed record SystemInfoDto(string Name, string Version, string Environment, DateTimeOffset ServerTimeUtc);

[Route(ApiRoutes.V1 + "/system")]
public sealed class SystemController(IHostEnvironment environment, TimeProvider timeProvider) : ApiControllerBase
{
    private static readonly string Version =
        typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";

    /// <summary>Basic service information. Used by the Angular app and smoke tests to confirm connectivity.</summary>
    [HttpGet("info")]
    [ProducesResponseType<ApiResponse<SystemInfoDto>>(StatusCodes.Status200OK)]
    public ActionResult<ApiResponse<SystemInfoDto>> GetInfo() =>
        OkResponse(new SystemInfoDto("InviteCraft API", Version, environment.EnvironmentName, timeProvider.GetUtcNow()));
}
