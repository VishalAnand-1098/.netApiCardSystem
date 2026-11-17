using System.Security.Claims;
using System.Text;
using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Application.Common.Settings;
using InviteCraft.Domain.Entities.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace InviteCraft.Infrastructure.Identity;

/// <summary>JWT settings. Bound from the "Jwt" section; the signing key must come from secrets in production.</summary>
public sealed class JwtSettings
{
    public const string SectionName = "Jwt";
    public const int MinimumKeyBytes = 32; // HS256 needs ≥ 256-bit keys
    public const string DevelopmentKeyMarker = "DEV-ONLY";

    public string Issuer { get; init; } = "InviteCraft";
    public string Audience { get; init; } = "InviteCraft.Web";
    public string SigningKey { get; init; } = string.Empty;

    public SymmetricSecurityKey CreateSigningKey() => new(Encoding.UTF8.GetBytes(SigningKey));
}

/// <summary>Claim names used in access tokens (short JWT names, inbound claim mapping disabled).</summary>
public static class AppClaims
{
    public const string Subject = JwtRegisteredClaimNames.Sub;
    public const string Email = JwtRegisteredClaimNames.Email;
    public const string Name = "name";
    public const string Role = "role";
    public const string EmailVerified = "email_verified";
}

internal sealed class JwtTokenService(
    IOptions<JwtSettings> jwtOptions,
    IOptions<AuthSettings> authOptions,
    TimeProvider timeProvider) : IAccessTokenService
{
    private static readonly JsonWebTokenHandler Handler = new();

    public AccessToken Create(User user, IReadOnlyCollection<string> roles)
    {
        var jwt = jwtOptions.Value;
        var now = timeProvider.GetUtcNow();
        var expires = now.AddMinutes(authOptions.Value.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(AppClaims.Subject, user.Id.ToString()),
            new(AppClaims.Email, user.Email),
            new(AppClaims.Name, user.FullName),
            new(AppClaims.EmailVerified, user.EmailVerifiedAt.HasValue ? "true" : "false", ClaimValueTypes.Boolean),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };
        claims.AddRange(roles.Select(role => new Claim(AppClaims.Role, role)));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(jwt.CreateSigningKey(), SecurityAlgorithms.HmacSha256),
        };

        return new AccessToken(Handler.CreateToken(descriptor), expires);
    }
}
