using InviteCraft.Domain.Common;
using InviteCraft.Domain.Enums;

namespace InviteCraft.Domain.Entities.Templates;

/// <summary>
/// One customer-editable element. Drives both the dynamic form (label, type, validation)
/// and the text box on the card (frame + style). Frame values are normalised 0–1 of the trim box.
/// </summary>
public class TemplateEditableField : AuditableEntity
{
    public Guid TemplateId { get; set; }
    /// <summary>Stable machine key, e.g. "bride_name". Unique within the template.</summary>
    public string FieldKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    /// <summary>JSON object of language → label, e.g. {"hi":"दुल्हन का नाम"}.</summary>
    public string? LabelTranslations { get; set; }
    public EditableFieldType FieldType { get; set; } = EditableFieldType.Text;
    /// <summary>Form section, e.g. "Couple", "Ceremony", "Venue".</summary>
    public string? GroupName { get; set; }
    public bool IsRequired { get; set; }
    public int? MaxLength { get; set; }
    public string? Placeholder { get; set; }
    public string? DefaultValue { get; set; }
    /// <summary>JSON array of options for Select fields.</summary>
    public string? Options { get; set; }
    /// <summary>Display format for Date/Time fields, e.g. "dddd, d MMMM yyyy".</summary>
    public string? DisplayFormat { get; set; }

    public int PageIndex { get; set; }
    public decimal X { get; set; }
    public decimal Y { get; set; }
    public decimal Width { get; set; }
    public decimal Height { get; set; }
    public decimal Rotation { get; set; }
    public int ZIndex { get; set; }

    /// <summary>JSON style: fontId, fontSizePt, color, align, lineHeight, letterSpacing, autoShrink…</summary>
    public string Style { get; set; } = "{}";
    /// <summary>JSON: what the customer may change (canChangeFont, canChangeSize, canChangeColor, canChangeAlign, canMove).</summary>
    public string Permissions { get; set; } = "{}";

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public Template Template { get; set; } = null!;
}
