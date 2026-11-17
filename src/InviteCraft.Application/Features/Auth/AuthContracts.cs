using InviteCraft.Domain.Entities.Identity;

namespace InviteCraft.Application.Features.Auth;

// ------------------------------------------------------------------ requests

public sealed record RegisterRequest(string FullName, string Email, string Password, string? Phone);

public sealed record LoginRequest(string Email, string Password);

public sealed record VerifyEmailRequest(string Token);

public sealed record ForgotPasswordRequest(string Email);

public sealed record ResetPasswordRequest(string Token, string NewPassword);

// ------------------------------------------------------------------ responses

public sealed record AuthUserDto(
    Guid Id,
    string Email,
    string FullName,
    string? Phone,
    string? AvatarUrl,
    bool EmailVerified,
    IReadOnlyList<string> Roles,
    DateTimeOffset CreatedAt);

/// <summary>Body returned to the client. The refresh token is never in the body — only in an HttpOnly cookie.</summary>
public sealed record AuthResponse(string AccessToken, DateTimeOffset ExpiresAt, AuthUserDto User);

/// <summary>Internal result of a sign-in/rotation; the API layer splits it into body + cookie.</summary>
public sealed record AuthResult(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    AuthUserDto User)
{
    public AuthResponse ToResponse() => new(AccessToken, AccessTokenExpiresAt, User);
}

// ------------------------------------------------------------------ mapping

public static class AuthMappings
{
    public static IReadOnlyList<string> GetRoleNames(this User user) =>
        user.UserRoles.Where(ur => ur.Role is not null).Select(ur => ur.Role.Name).OrderBy(n => n).ToList();

    public static AuthUserDto ToAuthUserDto(this User user) => new(
        user.Id,
        user.Email,
        user.FullName,
        user.Phone,
        user.AvatarUrl,
        user.EmailVerifiedAt.HasValue,
        user.GetRoleNames(),
        user.CreatedAt);

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public static string? NormalizePhone(string? phone) =>
        string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
}
