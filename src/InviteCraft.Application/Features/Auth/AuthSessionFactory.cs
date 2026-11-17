using System.Net;
using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Application.Common.Settings;
using InviteCraft.Domain.Entities.Identity;
using Microsoft.Extensions.Options;

namespace InviteCraft.Application.Features.Auth;

/// <summary>
/// Creates a signed-in session: a JWT access token plus a new refresh token row (hash only).
/// The caller saves the unit of work.
/// </summary>
public sealed class AuthSessionFactory(
    IAccessTokenService accessTokens,
    ISecureTokenGenerator tokenGenerator,
    IRefreshTokenRepository refreshTokens,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    IOptions<AuthSettings> settings)
{
    /// <param name="user">Must have UserRoles.Role loaded.</param>
    /// <param name="familyId">Existing family when rotating; null starts a new family (new sign-in).</param>
    public async Task<(AuthResult Result, RefreshToken Token)> CreateAsync(
        User user, Guid? familyId = null, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var roles = user.GetRoleNames();
        var access = accessTokens.Create(user, roles);
        var (rawRefresh, refreshHash) = tokenGenerator.Generate();

        var refresh = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = refreshHash,
            FamilyId = familyId ?? Guid.CreateVersion7(),
            ExpiresAt = now.AddDays(settings.Value.RefreshTokenDays),
            CreatedByIp = IPAddress.TryParse(currentUser.IpAddress, out var ip) ? ip : null,
        };
        await refreshTokens.AddAsync(refresh, cancellationToken);

        var result = new AuthResult(access.Token, access.ExpiresAt, rawRefresh, refresh.ExpiresAt, user.ToAuthUserDto());
        return (result, refresh);
    }

    /// <summary>Revokes every active refresh token of a user (password change/reset, account disable).</summary>
    public async Task RevokeAllAsync(Guid userId, string reason, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        foreach (var token in await refreshTokens.GetActiveByUserAsync(userId, now, cancellationToken))
        {
            token.RevokedAt = now;
            token.RevokedReason = reason;
        }
    }
}
