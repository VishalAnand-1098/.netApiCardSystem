using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Domain.Entities.Identity;
using InviteCraft.Domain.Enums;

namespace InviteCraft.Application.Features.Auth;

/// <remarks>Methods returning entities return them tracked, ready to modify and save.</remarks>
public interface IUserRepository : IRepository<User>
{
    Task<User?> GetByEmailWithRolesAsync(string normalizedEmail, CancellationToken cancellationToken = default);
    Task<User?> GetByIdWithRolesAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken = default);
    Task<Role?> GetRoleAsync(string roleName, CancellationToken cancellationToken = default);
}

public interface IRefreshTokenRepository : IRepository<RefreshToken>
{
    /// <summary>Includes the user and roles. Tokens of soft-deleted users are not returned.</summary>
    Task<RefreshToken?> GetByHashWithUserAsync(string tokenHash, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RefreshToken>> GetActiveByFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RefreshToken>> GetActiveByUserAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken = default);
}

public interface IUserTokenRepository : IRepository<UserToken>
{
    /// <summary>Includes the user (with roles).</summary>
    Task<UserToken?> GetByHashAsync(string tokenHash, TokenPurpose purpose, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserToken>> GetUnconsumedAsync(Guid userId, TokenPurpose purpose, CancellationToken cancellationToken = default);
}
