using InviteCraft.Domain.Common;
using InviteCraft.Domain.Entities.Commerce;
using InviteCraft.Domain.Entities.Invitations;

namespace InviteCraft.Domain.Entities.Identity;

public class User : SoftDeletableEntity
{
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? AvatarUrl { get; set; }
    public DateTimeOffset? EmailVerifiedAt { get; set; }
    public bool IsActive { get; set; } = true;
    public int FailedLoginCount { get; set; }
    public DateTimeOffset? LockoutEnd { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }

    public bool IsEmailVerified => EmailVerifiedAt.HasValue;

    public ICollection<UserRole> UserRoles { get; set; } = [];
    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
    public ICollection<Invitation> Invitations { get; set; } = [];
    public ICollection<Order> Orders { get; set; } = [];
}
