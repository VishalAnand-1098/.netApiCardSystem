using InviteCraft.Api.Contracts;
using InviteCraft.Domain.Common;
using InviteCraft.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace InviteCraft.Api.Infrastructure;

public static class AuthPolicies
{
    public const string AdminOnly = "AdminOnly";
}

internal static class AuthenticationSetup
{
    public static IServiceCollection AddApiAuthentication(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var jwt = configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>() ?? new JwtSettings();

        if (!environment.IsDevelopment() && jwt.SigningKey.Contains(JwtSettings.DevelopmentKeyMarker, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The development JWT signing key must not be used outside Development. Set Jwt__SigningKey from a secret store.");
        }

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false; // keep "sub", "role", "email" as issued
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = jwt.SigningKey.Length > 0 ? jwt.CreateSigningKey() : null,
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = AppClaims.Name,
                    RoleClaimType = AppClaims.Role,
                };

                options.Events = new JwtBearerEvents
                {
                    // Same envelope as every other error, instead of an empty 401/403.
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        var expired = context.AuthenticateFailure is SecurityTokenExpiredException;
                        var message = expired ? "Your session has expired. Please sign in again." : "Please sign in to continue.";
                        var code = expired ? "Auth.TokenExpired" : "Auth.Unauthorized";

                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        context.Response.Headers.WWWAuthenticate = expired
                            ? "Bearer error=\"invalid_token\", error_description=\"The token expired\""
                            : "Bearer";
                        await context.Response.WriteAsJsonAsync(ApiResponse.Fail(
                            message, [new ApiError(code, message)], RequestTrace.For(context.HttpContext)));
                    },
                    OnForbidden = async context =>
                    {
                        const string message = "You do not have permission to access this resource.";
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        await context.Response.WriteAsJsonAsync(ApiResponse.Fail(
                            message, [new ApiError("Auth.Forbidden", message)], RequestTrace.For(context.HttpContext)));
                    },
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.AdminOnly, policy => policy.RequireAuthenticatedUser().RequireRole(RoleNames.Admin));

        return services;
    }
}
