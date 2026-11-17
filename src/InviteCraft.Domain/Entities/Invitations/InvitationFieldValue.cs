using InviteCraft.Domain.Common;
using InviteCraft.Domain.Entities.Templates;

namespace InviteCraft.Domain.Entities.Invitations;

/// <summary>The customer's value for one template field, plus optional per-field style/position overrides.</summary>
public class InvitationFieldValue : AuditableEntity
{
    public Guid InvitationId { get; set; }
    public Guid FieldId { get; set; }
    /// <summary>Text value, ISO date/time for Date/Time fields, or a storage key for Image fields.</summary>
    public string? Value { get; set; }
    /// <summary>JSON style overrides (only keys the field's permissions allow).</summary>
    public string? StyleOverride { get; set; }
    /// <summary>JSON frame override {x,y,w,h,rotation} when the field allows moving.</summary>
    public string? FrameOverride { get; set; }

    public Invitation Invitation { get; set; } = null!;
    public TemplateEditableField Field { get; set; } = null!;
}
