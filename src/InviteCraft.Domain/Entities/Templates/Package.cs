using InviteCraft.Domain.Common;

namespace InviteCraft.Domain.Entities.Templates;

/// <summary>
/// Purchase tier (Free / Basic / Premium / Luxury / Custom). Defines what an unlocked invitation
/// is entitled to: formats, resolution, download count, watermark removal and edit window.
/// </summary>
public class Package : AuditableEntity
{
    public const string FreeCode = "FREE";
    public const string BasicCode = "BASIC";
    public const string PremiumCode = "PREMIUM";
    public const string LuxuryCode = "LUXURY";
    public const string CustomCode = "CUSTOM";

    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>JSON array of marketing bullet points.</summary>
    public string Features { get; set; } = "[]";
    /// <summary>Default price when a template has no specific TemplatePricing row for this package.</summary>
    public decimal BasePrice { get; set; }
    /// <summary>Values of DownloadFormat, e.g. ["Jpg","Png","Pdf"].</summary>
    /// <remarks>An array (not List) so seeded values compare structurally in migrations.</remarks>
    public string[] AllowedFormats { get; set; } = [];
    public int MaxDpi { get; set; } = 150;
    /// <summary>Null = unlimited.</summary>
    public int? MaxDownloads { get; set; }
    public bool RemovesWatermark { get; set; }
    /// <summary>Days after purchase during which the invitation can still be edited. Null = forever.</summary>
    public int? EditWindowDays { get; set; }
    public bool IncludesCustomDesign { get; set; }
    public bool IsRecommended { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Template-specific price for a package (overrides Package.BasePrice).</summary>
public class TemplatePricing : AuditableEntity
{
    public Guid TemplateId { get; set; }
    public Guid PackageId { get; set; }
    public decimal Price { get; set; }
    /// <summary>Optional "was" price shown struck through.</summary>
    public decimal? CompareAtPrice { get; set; }
    public string Currency { get; set; } = Currencies.Inr;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? ValidFrom { get; set; }
    public DateTimeOffset? ValidTo { get; set; }

    public Template Template { get; set; } = null!;
    public Package Package { get; set; } = null!;
}
