using InviteCraft.Api.Contracts;
using InviteCraft.Api.Infrastructure;
using InviteCraft.Application.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InviteCraft.Api.Controllers.Admin;

public sealed record AdminIdentityDto(Guid UserId, string? Email, bool IsAdmin);

/// <summary>
/// Admin-only diagnostics. Every controller under Controllers/Admin carries the AdminOnly policy,
/// so customers get 403 and anonymous callers 401.
/// </summary>
[Route(ApiRoutes.AdminV1 + "/system")]
[Authorize(Policy = AuthPolicies.AdminOnly)]
public sealed class AdminSystemController(ICurrentUser currentUser) : ApiControllerBase
{
    [HttpGet("whoami")]
    [ProducesResponseType<ApiResponse<AdminIdentityDto>>(StatusCodes.Status200OK)]
    public ActionResult<ApiResponse<AdminIdentityDto>> WhoAmI() =>
        OkResponse(new AdminIdentityDto(currentUser.UserId!.Value, currentUser.Email, currentUser.IsInRole("Admin")));
}
