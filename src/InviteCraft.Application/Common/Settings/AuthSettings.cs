namespace InviteCraft.Application.Common.Settings;

/// <summary>Authentication behaviour. Bound from the "Auth" configuration section.</summary>
public sealed class AuthSettings
{
    public const string SectionName = "Auth";

    public int AccessTokenMinutes { get; init; } = 15;
    public int RefreshTokenDays { get; init; } = 14;
    public int EmailVerificationTokenHours { get; init; } = 48;
    public int PasswordResetTokenMinutes { get; init; } = 60;
    public int MaxFailedLoginAttempts { get; init; } = 5;
    public int LockoutMinutes { get; init; } = 15;

    /// <summary>
    /// A rotated refresh token presented again within this window is treated as a benign race
    /// (e.g. two tabs refreshing at once) rather than token theft.
    /// </summary>
    public int RefreshReuseGraceSeconds { get; init; } = 30;

    /// <summary>Marks the refresh cookie Secure. Must be true in production (HTTPS).</summary>
    public bool RefreshCookieSecure { get; init; } = true;

    /// <summary>Base URL of the Angular app, used to build links in emails.</summary>
    public string FrontendBaseUrl { get; init; } = "http://localhost:4200";
}
