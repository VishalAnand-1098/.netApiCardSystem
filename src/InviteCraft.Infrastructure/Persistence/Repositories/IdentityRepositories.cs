using InviteCraft.Application.Features.Auth;
using InviteCraft.Domain.Entities.Identity;
using InviteCraft.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace InviteCraft.Infrastructure.Persistence.Repositories;

internal sealed class UserRepository(AppDbContext context) : Repository<User>(context), IUserRepository
{
    private IQueryable<User> WithRoles => Set.Include(u => u.UserRoles).ThenInclude(ur => ur.Role);

    public Task<User?> GetByEmailWithRolesAsync(string normalizedEmail, CancellationToken cancellationToken = default) =>
        WithRoles.FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

    public Task<User?> GetByIdWithRolesAsync(Guid id, CancellationToken cancellationToken = default) =>
        WithRoles.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    public Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken = default) =>
        Set.AnyAsync(u => u.Email == normalizedEmail, cancellationToken);

    public Task<Role?> GetRoleAsync(string roleName, CancellationToken cancellationToken = default) =>
        Context.Roles.FirstOrDefaultAsync(r => r.Name == roleName, cancellationToken);
}

internal sealed class RefreshTokenRepository(AppDbContext context) : Repository<RefreshToken>(context), IRefreshTokenRepository
{
    public Task<RefreshToken?> GetByHashWithUserAsync(string tokenHash, CancellationToken cancellationToken = default) =>
        Set.Include(t => t.User).ThenInclude(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

    public async Task<IReadOnlyList<RefreshToken>> GetActiveByFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        await Set.Where(t => t.FamilyId == familyId && t.RevokedAt == null && t.ExpiresAt > now).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<RefreshToken>> GetActiveByUserAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        await Set.Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > now).ToListAsync(cancellationToken);
}

internal sealed class UserTokenRepository(AppDbContext context) : Repository<UserToken>(context), IUserTokenRepository
{
    public Task<UserToken?> GetByHashAsync(string tokenHash, TokenPurpose purpose, CancellationToken cancellationToken = default) =>
        Set.Include(t => t.User).ThenInclude(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash && t.Purpose == purpose, cancellationToken);

    public async Task<IReadOnlyList<UserToken>> GetUnconsumedAsync(Guid userId, TokenPurpose purpose, CancellationToken cancellationToken = default) =>
        await Set.Where(t => t.UserId == userId && t.Purpose == purpose && t.ConsumedAt == null).ToListAsync(cancellationToken);
}
