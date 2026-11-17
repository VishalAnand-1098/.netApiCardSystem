using InviteCraft.Api.Contracts;
using InviteCraft.Api.Infrastructure;
using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Application.Common.Settings;
using InviteCraft.Application.Features.Account;
using InviteCraft.Application.Features.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace InviteCraft.Api.Controllers;

/// <summary>The signed-in user's own profile and password.</summary>
[Route(ApiRoutes.V1 + "/account")]
[Authorize]
public sealed class AccountController(
    IAccountService accounts,
    ICurrentUser currentUser,
    IOptions<AuthSettings> settings) : ApiControllerBase
{
    private Guid UserId => currentUser.UserId!.Value;

    [HttpGet("profile")]
    [ProducesResponseType<ApiResponse<AuthUserDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<AuthUserDto>>> GetProfile(CancellationToken cancellationToken) =>
        FromResult(await accounts.GetProfileAsync(UserId, cancellationToken));

    [HttpPut("profile")]
    [ProducesResponseType<ApiResponse<AuthUserDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<AuthUserDto>>> UpdateProfile(UpdateProfileRequest request, CancellationToken cancellationToken) =>
        FromResult(await accounts.UpdateProfileAsync(UserId, request, cancellationToken), "Your profile has been updated.");

    /// <summary>Changes the password, signs out all other devices and returns a fresh session for this one.</summary>
    [HttpPost("change-password")]
    [ProducesResponseType<ApiResponse<AuthResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var result = await accounts.ChangePasswordAsync(UserId, request, cancellationToken);
        if (result.IsFailure)
            return ErrorResponse(result.Errors);

        RefreshTokenCookie.Write(Response, result.Value, settings.Value);
        return OkResponse(result.Value.ToResponse(), "Your password has been changed. Other devices have been signed out.");
    }
}
