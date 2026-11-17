using InviteCraft.Domain.Common;
using InviteCraft.Domain.Entities.Templates;
using InviteCraft.Domain.Enums;

namespace InviteCraft.Domain.Entities.Catalog;

public class Category : SoftDeletableEntity
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public CategoryKind Kind { get; set; }
    public string? Description { get; set; }
    /// <summary>Bootstrap Icons class, e.g. "bi-flower1".</summary>
    public string? Icon { get; set; }
    public string? ImageUrl { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public bool ShowOnHome { get; set; }
    public string? SeoTitle { get; set; }
    public string? SeoDescription { get; set; }

    public ICollection<SubCategory> SubCategories { get; set; } = [];
    public ICollection<Template> Templates { get; set; } = [];
}
