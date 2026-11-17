using System.Net;
using InviteCraft.Domain.Common;
using InviteCraft.Domain.Entities.Commerce;
using InviteCraft.Domain.Entities.Identity;
using InviteCraft.Domain.Entities.Templates;
using InviteCraft.Domain.Enums;

namespace InviteCraft.Domain.Entities.Invitations;

/// <summary>A render/download request. Doubles as the render job record (Queued → Rendering → Ready).</summary>
public class Download : CreationAuditedEntity
{
    public Guid UserId { get; set; }
    public Guid InvitationId { get; set; }
    /// <summary>The purchase that entitles this download; null for free/watermarked previews.</summary>
    public Guid? OrderItemId { get; set; }
    public DownloadFormat Format { get; set; }
    public int Dpi { get; set; }
    public bool HasWatermark { get; set; }
    public DownloadStatus Status { get; set; } = DownloadStatus.Queued;
    public string? StorageKey { get; set; }
    public long? FileSizeBytes { get; set; }
    public string? Error { get; set; }
    public IPAddress? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }

    public User User { get; set; } = null!;
    public Invitation Invitation { get; set; } = null!;
    public OrderItem? OrderItem { get; set; }
}

/// <summary>Customer bookmark ("Saved Templates").</summary>
public class SavedTemplate
{
    public Guid UserId { get; set; }
    public Guid TemplateId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public User User { get; set; } = null!;
    public Template Template { get; set; } = null!;
}
