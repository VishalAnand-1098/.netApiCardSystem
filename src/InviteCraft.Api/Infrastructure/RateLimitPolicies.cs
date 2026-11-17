namespace InviteCraft.Api.Infrastructure;

/// <summary>Named rate-limit policies, applied with <c>[EnableRateLimiting(RateLimitPolicies.Auth)]</c>.</summary>
public static class RateLimitPolicies
{
    public const string Auth = "auth";
    public const string Payments = "payments";
    public const string Uploads = "uploads";
    public const string Renders = "renders";
}

public sealed class RateLimitingSettings
{
    public const string SectionName = "RateLimiting";

    public int GlobalPermitPerMinute { get; init; } = 300;
    public int AuthPermitPerMinute { get; init; } = 10;
    public int PaymentPermitPerMinute { get; init; } = 20;
    public int UploadPermitPerMinute { get; init; } = 30;
    /// <summary>Creating downloads is CPU-heavy (a Chromium render each), so it is limited per user.</summary>
    public int RenderPermitPerMinute { get; init; } = 10;
}

public sealed class CorsSettings
{
    public const string SectionName = "Cors";
    public const string PolicyName = "SpaClient";

    public string[] AllowedOrigins { get; init; } = [];
}
