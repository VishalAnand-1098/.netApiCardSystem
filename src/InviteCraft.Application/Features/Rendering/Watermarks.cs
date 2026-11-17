using System.Text.Json;
using InviteCraft.Application.Features.Content;

namespace InviteCraft.Application.Features.Rendering;

/// <summary>The preview watermark, admin-tunable through the "watermark" site setting.</summary>
public static class Watermarks
{
    public const string DefaultText = "InviteCraft · Preview";
    public const decimal DefaultOpacity = 0.22m;

    public static async Task<WatermarkSpec> ReadAsync(IContentRepository content, CancellationToken cancellationToken)
    {
        try
        {
            var json = await content.GetSettingJsonAsync("watermark", cancellationToken);
            if (json is not null)
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                var text = root.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
                var opacity = root.TryGetProperty("opacity", out var o) && o.TryGetDecimal(out var value) ? value : DefaultOpacity;
                if (!string.IsNullOrWhiteSpace(text))
                    return new WatermarkSpec(text, Math.Clamp(opacity, 0.05m, 0.6m));
            }
        }
        catch (JsonException)
        {
            // Malformed setting: use the defaults.
        }

        return new WatermarkSpec(DefaultText, DefaultOpacity);
    }
}
