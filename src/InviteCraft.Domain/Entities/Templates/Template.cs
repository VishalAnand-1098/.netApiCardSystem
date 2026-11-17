using InviteCraft.Domain.Common;
using InviteCraft.Domain.Entities.Catalog;
using InviteCraft.Domain.Enums;

namespace InviteCraft.Domain.Entities.Templates;

/// <summary>
/// A sellable card design. Static artwork lives in <see cref="Design"/> (JSONB, see ARCHITECTURE.md §5.2);
/// customer-editable text boxes are <see cref="EditableFields"/> rows.
/// </summary>
public class Template : SoftDeletableEntity
{
    /// <summary>Human-friendly id shown to admins and customers, e.g. "IC-10023".</summary>
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public Guid CategoryId { get; set; }
    public Guid? SubCategoryId { get; set; }
    public string? Description { get; set; }
    public TemplateTier Tier { get; set; } = TemplateTier.Free;
    public TemplateOrientation Orientation { get; set; } = TemplateOrientation.Portrait;

    /// <summary>Trim size of the finished card in millimetres.</summary>
    public decimal WidthMm { get; set; }
    public decimal HeightMm { get; set; }
    public int Dpi { get; set; } = 300;
    public decimal BleedMm { get; set; } = 3m;

    /// <summary>ISO 639-1 codes, e.g. ["en", "hi", "mr"].</summary>
    public List<string> SupportedLanguages { get; set; } = [];
    public List<string> Tags { get; set; } = [];

    /// <summary>Layout document (JSON): canvas, pages, static layers.</summary>
    public string Design { get; set; } = "{}";
    /// <summary>Incremented on every published layout change; invitations snapshot it.</summary>
    public int LayoutVersion { get; set; } = 1;

    public TemplateStatus Status { get; set; } = TemplateStatus.Draft;
    public bool IsFeatured { get; set; }
    /// <summary>Denormalised lowest active price, for listing and sorting without joins.</summary>
    public decimal? StartingPrice { get; set; }
    public long ViewCount { get; set; }
    public int SalesCount { get; set; }
    public decimal AvgRating { get; set; }
    public int ReviewCount { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary>Optimistic concurrency token (PostgreSQL xmin).</summary>
    public uint Version { get; set; }

    public Category Category { get; set; } = null!;
    public SubCategory? SubCategory { get; set; }
    public ICollection<TemplateAsset> Assets { get; set; } = [];
    public ICollection<TemplateEditableField> EditableFields { get; set; } = [];
    public ICollection<TemplateFontConfiguration> FontConfigurations { get; set; } = [];
    public ICollection<TemplatePricing> Pricing { get; set; } = [];
}
