using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace InviteCraft.Application.Common.Text;

/// <summary>Creates URL slugs: "Griha Pravesh & Puja" → "griha-pravesh-puja", "Café Noël" → "cafe-noel".</summary>
public static partial class SlugGenerator
{
    public const int MaxLength = 120;

    /// <summary>Valid slug: lowercase letters, digits and single hyphens, not starting/ending with a hyphen.</summary>
    public static bool IsValid(string? slug) =>
        !string.IsNullOrEmpty(slug) && slug.Length <= MaxLength && SlugPattern().IsMatch(slug);

    /// <summary>
    /// Returns an ASCII slug, or an empty string when the text has no Latin letters or digits
    /// (e.g. a purely Devanagari name) — callers then require an explicit slug.
    /// </summary>
    public static string Generate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var normalized = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue; // drop accents
            builder.Append(ch is >= 'a' and <= 'z' or >= '0' and <= '9' ? ch : '-');
        }

        var slug = MultipleHyphens().Replace(builder.ToString(), "-").Trim('-');
        return slug.Length <= MaxLength ? slug : slug[..MaxLength].TrimEnd('-');
    }

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();

    [GeneratedRegex("-{2,}")]
    private static partial Regex MultipleHyphens();
}
