namespace InviteCraft.Application.Features.Templates;

// Typed model of the layout document stored in templates.design (JSONB) and of the per-field
// style/permissions blobs. See docs/ARCHITECTURE.md §5. All frames are normalised (0–1) to the
// trim box and font sizes are points at physical card size, so layouts are resolution-independent.

/// <summary>Static artwork of a template: canvas settings and, per page, a background plus fixed layers.</summary>
public sealed record TemplateDesign(int SchemaVersion, DesignCanvas Canvas, IReadOnlyList<DesignPage> Pages)
{
    public const int CurrentSchemaVersion = 1;

    public static TemplateDesign Empty() =>
        new(CurrentSchemaVersion, new DesignCanvas("#FFFFFF"), [new DesignPage(0, null, [])]);

    public IEnumerable<Guid> ReferencedAssetIds() =>
        Pages.SelectMany(p => p.Layers.Where(l => l.AssetId.HasValue).Select(l => l.AssetId!.Value)
            .Concat(p.Background?.AssetId is { } bg ? [bg] : []));

    public IEnumerable<Guid> ReferencedFontIds() =>
        Pages.SelectMany(p => p.Layers).Where(l => l.Style?.FontId is not null).Select(l => l.Style!.FontId!.Value);
}

public sealed record DesignCanvas(string? BackgroundColor);

public sealed record DesignPage(int Index, DesignBackground? Background, IReadOnlyList<DesignLayer> Layers);

/// <param name="Fit">cover | contain | stretch</param>
public sealed record DesignBackground(Guid? AssetId, string? Color, string Fit = "cover");

/// <summary>
/// A fixed (non-editable) element. Type "text" needs Text + Style; "image" needs AssetId;
/// "shape" needs Shape (rect | ellipse | line) and Fill.
/// </summary>
public sealed record DesignLayer(
    string Id,
    string Type,
    LayerFrame Frame,
    int ZIndex,
    decimal Opacity = 1,
    Guid? AssetId = null,
    string? Text = null,
    TextStyle? Style = null,
    string? Shape = null,
    string? Fill = null);

/// <summary>Position and size as fractions of the trim box; rotation in degrees.</summary>
public sealed record LayerFrame(decimal X, decimal Y, decimal W, decimal H, decimal Rotation = 0);

/// <param name="Align">left | center | right | justify</param>
/// <param name="TextTransform">none | uppercase | lowercase | capitalize</param>
/// <param name="AutoShrink">Shrink the font (down to 60%) when the text would overflow its frame.</param>
public sealed record TextStyle(
    Guid? FontId,
    decimal FontSizePt,
    string Color,
    string Align = "center",
    decimal LineHeight = 1.2m,
    decimal LetterSpacing = 0,
    int FontWeight = 400,
    bool Italic = false,
    string TextTransform = "none",
    bool AutoShrink = true);

/// <summary>What a customer may change on an editable field.</summary>
public sealed record FieldPermissions(
    bool CanChangeFont = true,
    bool CanChangeSize = true,
    bool CanChangeColor = true,
    bool CanChangeAlign = true,
    bool CanMove = false)
{
    public static FieldPermissions Default { get; } = new();
}
