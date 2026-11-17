using InviteCraft.Domain.Common;
using InviteCraft.Domain.Entities.Templates;

namespace InviteCraft.Domain.Entities.Catalog;

public class SubCategory : SoftDeletableEntity
{
    public Guid CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>Unique within its category, so "wedding" can exist under both Hindu and Christian ceremonies.</summary>
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public Category Category { get; set; } = null!;
    public ICollection<Template> Templates { get; set; } = [];
}
