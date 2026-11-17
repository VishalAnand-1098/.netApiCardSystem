using System.Net;
using InviteCraft.Domain.Common;

namespace InviteCraft.Domain.Entities.Identity;

/// <summary>
/// Opaque refresh token (only its SHA-256 hash is stored). Tokens issued by rotation share a
/// <see cref="FamilyId"/>; presenting an already-rotated token revokes the whole family (reuse detection).
/// </summary>
public class RefreshToken : CreationAuditedEntity
{
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public Guid FamilyId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public IPAddress? CreatedByIp { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }
    public Guid? ReplacedByTokenId { get; set; }

    public bool IsRevoked => RevokedAt.HasValue;
    public bool IsActive(DateTimeOffset now) => !IsRevoked && ExpiresAt > now;

    public User User { get; set; } = null!;
    public RefreshToken? ReplacedByToken { get; set; }
}
