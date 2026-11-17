using System.Globalization;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;
using InviteCraft.Application.Features.Templates;
using InviteCraft.Domain.Entities.Templates;
using InviteCraft.Domain.Enums;

namespace InviteCraft.Application.Features.Invitations;

/// <summary>
/// What a customer may put into a template field and how they may restyle it. The template's configuration
/// (type, max length, options, permissions) is the single source of truth — the editor mirrors it for UX,
/// but the server enforces it.
/// </summary>
internal static partial class FieldValueRules
{
    public const int AbsoluteMaxLength = 4000;
    public static readonly string[] Aligns = ["left", "center", "right", "justify"];

    [GeneratedRegex(@"^\+?[0-9][0-9\s\-()]{5,19}$")]
    private static partial Regex PhonePattern();

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColor();

    /// <summary>Validates and normalises a raw value. Empty input is valid and becomes null.</summary>
    public static (string? Value, string? Error) CheckValue(TemplateEditableField field, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (null, null);

        // NFC keeps equivalent Indic/Arabic sequences identical, so the same name never renders two ways.
        var value = raw.Normalize(NormalizationForm.FormC).Trim();
        var multiline = field.FieldType == EditableFieldType.MultilineText;

        if (field.FieldType != EditableFieldType.Image)
        {
            value = multiline
                ? value.Replace("\r\n", "\n").Replace('\r', '\n')
                : NewLines().Replace(value, " ");

            if (value.Any(c => char.IsControl(c) && c != '\n'))
                return (null, "Contains characters that are not supported.");
        }

        var max = Math.Min(field.MaxLength ?? AbsoluteMaxLength, AbsoluteMaxLength);
        if (value.Length > max)
            return (null, $"Must be at most {max} characters.");

        switch (field.FieldType)
        {
            case EditableFieldType.Date:
                if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) || date.Year is < 1900 or > 2100)
                    return (null, "Enter a valid date.");
                break;

            case EditableFieldType.Time:
                if (!TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                    return (null, "Enter a valid time.");
                break;

            case EditableFieldType.Phone:
                if (!PhonePattern().IsMatch(value))
                    return (null, "Enter a valid phone number.");
                break;

            case EditableFieldType.Email:
                if (!MailAddress.TryCreate(value, out var mail) || mail.Address != value || !value.Contains('.'))
                    return (null, "Enter a valid email address.");
                break;

            case EditableFieldType.Number:
                if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
                    return (null, "Enter a valid number.");
                break;

            case EditableFieldType.Select:
                var options = TemplateJson.Deserialize<List<string>>(field.Options) ?? [];
                if (!options.Contains(value))
                    return (null, "Choose one of the available options.");
                break;

            case EditableFieldType.Image:
                if (!Guid.TryParse(value, out _))
                    return (null, "Upload the image first.");
                break;
        }

        return (value, null);
    }

    /// <summary>Checks a style override against the field's permissions and the template's font list.</summary>
    public static string? CheckStyle(FieldStyleOverride? style, TemplateEditableField field, FieldPermissions permissions, IReadOnlySet<Guid> templateFontIds)
    {
        if (style is null || (style.FontId is null && style.FontSizePt is null && style.Color is null && style.Align is null))
            return null;

        if (field.FieldType == EditableFieldType.Image)
            return "Image fields cannot be restyled.";

        var baseStyle = TemplateJson.Deserialize<TextStyle>(field.Style);

        if (style.FontId is { } fontId && (!permissions.CanChangeFont || !templateFontIds.Contains(fontId)))
            return permissions.CanChangeFont ? "That font is not available for this design." : "The font cannot be changed for this field.";

        if (style.FontSizePt is { } size)
        {
            if (!permissions.CanChangeSize)
                return "The size cannot be changed for this field.";

            var basePt = baseStyle?.FontSizePt ?? 12m;
            var min = Math.Max(4m, basePt * 0.5m);
            var max = Math.Min(200m, basePt * 2m);
            if (size < min || size > max)
                return $"Font size must be between {min:0.#} and {max:0.#} pt.";
        }

        if (style.Color is { } color)
        {
            if (!permissions.CanChangeColor)
                return "The colour cannot be changed for this field.";
            if (!HexColor().IsMatch(color))
                return "Colour must be in #RRGGBB format.";
        }

        if (style.Align is { } align)
        {
            if (!permissions.CanChangeAlign)
                return "The alignment cannot be changed for this field.";
            if (!Aligns.Contains(align))
                return "Alignment must be left, center, right or justify.";
        }

        return null;
    }

    public static string? CheckPosition(FieldPosition? position, TemplateEditableField field, FieldPermissions permissions)
    {
        if (position is null)
            return null;
        if (!permissions.CanMove)
            return "This field cannot be moved.";

        const decimal tolerance = 0.0001m;
        if (position.X < 0 || position.Y < 0 || position.X + field.Width > 1 + tolerance || position.Y + field.Height > 1 + tolerance)
            return "The field must stay inside the card.";
        return null;
    }

    /// <summary>Stored form of a style override: null when nothing is overridden, colours lower-cased.</summary>
    public static FieldStyleOverride? NormaliseStyle(FieldStyleOverride? style) =>
        style is null || (style.FontId is null && style.FontSizePt is null && style.Color is null && style.Align is null)
            ? null
            : style with { Color = style.Color?.ToLowerInvariant(), FontSizePt = style.FontSizePt is { } s ? decimal.Round(s, 1) : null };

    public static FieldPosition? NormalisePosition(FieldPosition? position) =>
        position is null ? null : new FieldPosition(decimal.Round(position.X, 4), decimal.Round(position.Y, 4));

    [GeneratedRegex(@"\s*[\r\n]+\s*")]
    private static partial Regex NewLines();
}
