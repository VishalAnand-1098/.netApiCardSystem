using InviteCraft.Domain.Common;
using InviteCraft.Domain.Enums;

namespace InviteCraft.Domain.Entities.Templates;

/// <summary>Global font library entry. Self-hosted so browser preview and server rendering match exactly.</summary>
public class Font : AuditableEntity
{
    public string FamilyName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    /// <summary>Supported scripts, e.g. ["latin", "devanagari"]. Used to offer only fonts that can render the chosen language.</summary>
    public List<string> Scripts { get; set; } = [];
    public int Weight { get; set; } = 400;
    public string Style { get; set; } = "normal";
    public string Woff2Key { get; set; } = string.Empty;
    public string? TtfKey { get; set; }
    public string? License { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Whitelists a font for a template, with its role in the design.</summary>
public class TemplateFontConfiguration : Entity
{
    public Guid TemplateId { get; set; }
    public Guid FontId { get; set; }
    public FontRole Role { get; set; } = FontRole.Body;
    public bool IsDefault { get; set; }
    public int SortOrder { get; set; }

    public Template Template { get; set; } = null!;
    public Font Font { get; set; } = null!;
}
