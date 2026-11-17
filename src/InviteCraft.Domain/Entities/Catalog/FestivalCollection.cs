using InviteCraft.Domain.Common;
using InviteCraft.Domain.Entities.Templates;

namespace InviteCraft.Domain.Entities.Catalog;

/// <summary>Curated, time-boxed template collection (e.g. "Diwali 2026 Specials").</summary>
public class FestivalCollection : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? BannerImageUrl { get; set; }
    public string? ThemeColor { get; set; }
    public DateTimeOffset? StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public bool ShowOnHome { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<FestivalCollectionTemplate> Templates { get; set; } = [];
}

public class FestivalCollectionTemplate
{
    public Guid CollectionId { get; set; }
    public Guid TemplateId { get; set; }
    public int SortOrder { get; set; }

    public FestivalCollection Collection { get; set; } = null!;
    public Template Template { get; set; } = null!;
}
