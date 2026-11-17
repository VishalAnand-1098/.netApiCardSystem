using InviteCraft.Application.Common.Models;

namespace InviteCraft.Application.Features.Auth;

/// <summary>Stable error codes the Angular client can switch on.</summary>
public static class AuthErrors
{
    // Deliberately identical for "unknown email" and "wrong password" (no account enumeration).
    public static readonly Error InvalidCredentials =
        Error.Unauthorized("Auth.InvalidCredentials", "Invalid email or password.");

    public static readonly Error AccountDisabled =
        Error.Forbidden("Auth.AccountDisabled", "This account has been disabled. Please contact support.");

    public static readonly Error EmailTaken =
        Error.Conflict("Auth.EmailTaken", "An account with this email already exists.");

    public static readonly Error InvalidRefreshToken =
        Error.Unauthorized("Auth.SessionExpired", "Your session has expired. Please sign in again.");

    public static readonly Error InvalidOrExpiredLink =
        Error.Validation("Auth.InvalidToken", "This link is invalid or has expired. Please request a new one.", "token");

    public static readonly Error AlreadyVerified =
        Error.Conflict("Auth.AlreadyVerified", "Your email address is already verified.");

    public static readonly Error UserNotFound =
        Error.NotFound("User.NotFound", "User not found.");

    public static readonly Error WrongCurrentPassword =
        Error.Validation("Auth.WrongPassword", "Your current password is incorrect.", "currentPassword");

    public static Error LockedOut(int minutes) =>
        Error.Forbidden("Auth.LockedOut", $"Too many failed sign-in attempts. Please try again in {minutes} minute{(minutes == 1 ? "" : "s")}.");
}
