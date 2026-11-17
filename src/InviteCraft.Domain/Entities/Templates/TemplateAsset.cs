using InviteCraft.Domain.Common;
using InviteCraft.Domain.Enums;

namespace InviteCraft.Domain.Entities.Templates;

public class TemplateAsset : CreationAuditedEntity
{
    public Guid TemplateId { get; set; }
    public TemplateAssetType AssetType { get; set; }
    public AssetVariant Variant { get; set; } = AssetVariant.Original;
    /// <summary>Key in IFileStorage. Originals are private and only read by the server-side renderer.</summary>
    public string StorageKey { get; set; } = string.Empty;
    /// <summary>Browser-facing URL; null for private originals.</summary>
    public string? PublicUrl { get; set; }
    public string MimeType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public int? WidthPx { get; set; }
    public int? HeightPx { get; set; }
    public int PageIndex { get; set; }
    public int SortOrder { get; set; }

    public Template Template { get; set; } = null!;
}
