using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Application.Features.Invitations;
using InviteCraft.Application.Features.Templates;
using InviteCraft.Domain.Entities.Invitations;
using InviteCraft.Domain.Entities.Templates;
using InviteCraft.Domain.Enums;

namespace InviteCraft.Application.Features.Rendering;

/// <summary>Turns a stored invitation into the per-field content the renderer draws (same shape the editor uses).</summary>
internal static class RenderValueMapper
{
    public static IReadOnlyDictionary<string, RenderValue> FromInvitation(Invitation invitation, Template template, IFileStorage storage)
    {
        var fields = template.EditableFields.Where(f => f.IsActive).ToDictionary(f => f.Id);
        var values = new Dictionary<string, RenderValue>();

        foreach (var stored in invitation.FieldValues)
        {
            if (!fields.TryGetValue(stored.FieldId, out var field))
                continue;

            var imageUrl = field.FieldType == EditableFieldType.Image && Guid.TryParse(stored.Value, out var assetId)
                ? storage.GetPublicUrl(InvitationStorageKeys.Web(invitation.Id, assetId))
                : null;

            values[field.FieldKey] = new RenderValue(
                stored.Value, imageUrl,
                TemplateJson.Deserialize<FieldStyleOverride>(stored.StyleOverride),
                TemplateJson.Deserialize<FieldPosition>(stored.FrameOverride));
        }

        return values;
    }

    /// <summary>True when every required active field has content.</summary>
    public static bool IsComplete(Invitation invitation, Template template)
    {
        var filled = invitation.FieldValues.Where(v => !string.IsNullOrWhiteSpace(v.Value)).Select(v => v.FieldId).ToHashSet();
        return template.EditableFields.Where(f => f.IsActive && f.IsRequired).All(f => filled.Contains(f.Id));
    }

    /// <summary>Marketplace-thumbnail content: each field's default or placeholder, else a believable sample for its type.</summary>
    public static IReadOnlyDictionary<string, RenderValue> Sample(TemplateDetailDto template)
    {
        var values = new Dictionary<string, RenderValue>();
        foreach (var field in template.Fields.Where(f => f.IsActive))
        {
            var text = !string.IsNullOrWhiteSpace(field.DefaultValue) ? field.DefaultValue
                : !string.IsNullOrWhiteSpace(field.Placeholder) ? field.Placeholder
                : field.FieldType switch
                {
                    EditableFieldType.Date => "2027-02-14",
                    EditableFieldType.Time => "18:30",
                    EditableFieldType.Phone => "+91 98765 43210",
                    EditableFieldType.Email => "hello@example.com",
                    EditableFieldType.Number => "5",
                    EditableFieldType.Select => field.Options?.FirstOrDefault(),
                    EditableFieldType.Image => null,
                    _ => field.IsRequired ? field.Label : null,
                };

            if (!string.IsNullOrWhiteSpace(text))
                values[field.FieldKey] = new RenderValue(text, null, null, null);
        }

        return values;
    }
}
