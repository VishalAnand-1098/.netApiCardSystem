using InviteCraft.Domain.Common;
using InviteCraft.Domain.Enums;

namespace InviteCraft.Domain.Entities.Identity;

/// <summary>Single-use token for email verification or password reset (hash stored, never the raw value).</summary>
public class UserToken : CreationAuditedEntity
{
    public Guid UserId { get; set; }
    public TokenPurpose Purpose { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }

    public bool IsUsable(DateTimeOffset now) => ConsumedAt is null && ExpiresAt > now;

    public User User { get; set; } = null!;
}
