using InviteCraft.Domain.Common;
using InviteCraft.Domain.Entities.Identity;
using InviteCraft.Domain.Entities.Templates;
using InviteCraft.Domain.Enums;

namespace InviteCraft.Domain.Entities.Invitations;

/// <summary>
/// A customer's card: a template plus their content. Purchases unlock an invitation
/// (not a template) at a package tier — see ARCHITECTURE.md decision D4.
/// </summary>
public class Invitation : SoftDeletableEntity
{
    public Guid UserId { get; set; }
    public Guid TemplateId { get; set; }
    public string Title { get; set; } = string.Empty;
    /// <summary>ISO 639-1 language the content is written in.</summary>
    public string Language { get; set; } = "en";
    public InvitationStatus Status { get; set; } = InvitationStatus.Draft;
    /// <summary>Template layout version this invitation was designed against.</summary>
    public int LayoutVersion { get; set; } = 1;
    /// <summary>JSON: card-wide overrides (e.g. accent colour) where the template allows them.</summary>
    public string? StyleOverrides { get; set; }

    public InvitationVisibility Visibility { get; set; } = InvitationVisibility.Private;
    /// <summary>22-char random slug (~131 bits) used in public share URLs: /i/{ShareSlug}.</summary>
    public string? ShareSlug { get; set; }
    public DateTimeOffset? ShareExpiresAt { get; set; }

    public Guid? UnlockedPackageId { get; set; }
    public DateTimeOffset? UnlockedAt { get; set; }
    public DateTimeOffset? EditableUntil { get; set; }

    public string? PreviewImageKey { get; set; }
    public DateTimeOffset? LastRenderedAt { get; set; }
    public long ViewCount { get; set; }
    public Guid? DuplicatedFromId { get; set; }

    /// <summary>Optimistic concurrency token (PostgreSQL xmin).</summary>
    public uint Version { get; set; }

    public bool IsUnlocked => UnlockedPackageId.HasValue;

    /// <summary>Applies a purchase: the package's entitlements start now and the editing window (if the package has one) starts counting.</summary>
    public void Unlock(Package package, DateTimeOffset now)
    {
        UnlockedPackageId = package.Id;
        UnlockedAt = now;
        EditableUntil = package.EditWindowDays is { } days ? now.AddDays(days) : null;
    }

    /// <summary>Reverses a purchase (full refund): the invitation and its content stay, the entitlements go.</summary>
    public void Lock()
    {
        UnlockedPackageId = null;
        UnlockedAt = null;
        EditableUntil = null;
    }

    public User User { get; set; } = null!;
    public Template Template { get; set; } = null!;
    public Package? UnlockedPackage { get; set; }
    public Invitation? DuplicatedFrom { get; set; }
    public ICollection<InvitationFieldValue> FieldValues { get; set; } = [];
    public ICollection<Download> Downloads { get; set; } = [];
}
