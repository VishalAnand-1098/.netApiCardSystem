using InviteCraft.Api.Contracts;
using InviteCraft.Api.Infrastructure;
using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Application.Common.Models;
using InviteCraft.Application.Common.Settings;
using InviteCraft.Application.Features.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace InviteCraft.Api.Controllers;

/// <summary>
/// Registration, sign-in, token refresh and account recovery. Endpoints are anonymous unless marked
/// [Authorize] (no class-level [AllowAnonymous]: it would silently override the per-action [Authorize]).
/// </summary>
[Route(ApiRoutes.V1 + "/auth")]
public sealed class AuthController(
    IAuthService auth,
    ICurrentUser currentUser,
    IOptions<AuthSettings> settings) : ApiControllerBase
{
    /// <summary>Creates a customer account, sends a verification email and signs the user in.</summary>
    [HttpPost("register")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType<ApiResponse<AuthResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Register(RegisterRequest request, CancellationToken cancellationToken) =>
        SessionResponse(await auth.RegisterAsync(request, cancellationToken),
            "Your account has been created. Please check your inbox to verify your email address.");

    [HttpPost("login")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType<ApiResponse<AuthResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Login(LoginRequest request, CancellationToken cancellationToken) =>
        SessionResponse(await auth.LoginAsync(request, cancellationToken));

    /// <summary>Exchanges the refresh-token cookie for a new access token (and rotates the cookie).</summary>
    [HttpPost("refresh")]
    [ProducesResponseType<ApiResponse<AuthResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Refresh(CancellationToken cancellationToken)
    {
        var result = await auth.RefreshAsync(RefreshTokenCookie.Read(Request), cancellationToken);
        if (result.IsFailure)
            RefreshTokenCookie.Delete(Response, settings.Value);
        return SessionResponse(result);
    }

    [HttpPost("logout")]
    public async Task<ActionResult> Logout(CancellationToken cancellationToken)
    {
        var result = await auth.LogoutAsync(RefreshTokenCookie.Read(Request), cancellationToken);
        RefreshTokenCookie.Delete(Response, settings.Value);
        return FromResult(result, "You have been signed out.");
    }

    [HttpPost("verify-email")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<ActionResult> VerifyEmail(VerifyEmailRequest request, CancellationToken cancellationToken) =>
        FromResult(await auth.VerifyEmailAsync(request, cancellationToken), "Your email address has been verified.");

    [HttpPost("resend-verification")]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<ActionResult> ResendVerification(CancellationToken cancellationToken) =>
        FromResult(await auth.ResendVerificationAsync(currentUser.UserId!.Value, cancellationToken),
            "A new verification link has been sent to your email.");

    /// <summary>Always returns success so the response never reveals whether an account exists.</summary>
    [HttpPost("forgot-password")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<ActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken cancellationToken) =>
        FromResult(await auth.ForgotPasswordAsync(request, cancellationToken),
            "If an account exists for that email, a password reset link is on its way.");

    [HttpPost("reset-password")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<ActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken cancellationToken) =>
        FromResult(await auth.ResetPasswordAsync(request, cancellationToken),
            "Your password has been reset. Please sign in with your new password.");

    private ActionResult<ApiResponse<AuthResponse>> SessionResponse(Result<AuthResult> result, string? message = null)
    {
        if (result.IsFailure)
            return ErrorResponse(result.Errors);

        RefreshTokenCookie.Write(Response, result.Value, settings.Value);
        return OkResponse(result.Value.ToResponse(), message);
    }
}
