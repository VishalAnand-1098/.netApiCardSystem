using System.Text.RegularExpressions;
using FluentValidation;
using InviteCraft.Application.Common.Validation;
using InviteCraft.Application.Features.Categories;
using InviteCraft.Domain.Enums;

namespace InviteCraft.Application.Features.Templates;

internal static partial class DesignRules
{
    public static readonly string[] Aligns = ["left", "center", "right", "justify"];
    public static readonly string[] Transforms = ["none", "uppercase", "lowercase", "capitalize"];
    public static readonly string[] LayerTypes = ["text", "image", "shape"];
    public static readonly string[] Shapes = ["rect", "ellipse", "line"];
    public static readonly string[] Fits = ["cover", "contain", "stretch"];
    public const int MaxPages = 4;
    public const int MaxLayersPerPage = 80;
    public const int MaxFields = 60;

    [GeneratedRegex("^#(?:[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$")]
    public static partial Regex HexColor();

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]{0,63}$")]
    public static partial Regex LayerId();

    [GeneratedRegex("^[a-z][a-z0-9_]{1,63}$")]
    public static partial Regex FieldKey();

    [GeneratedRegex("^[a-z]{2,3}$")]
    public static partial Regex LanguageCode();
}

internal sealed class LayerFrameValidator : AppValidator<LayerFrame>
{
    public LayerFrameValidator()
    {
        RuleFor(f => f.X).InclusiveBetween(0, 1);
        RuleFor(f => f.Y).InclusiveBetween(0, 1);
        RuleFor(f => f.W).GreaterThan(0).LessThanOrEqualTo(1);
        RuleFor(f => f.H).GreaterThan(0).LessThanOrEqualTo(1);
        RuleFor(f => f.Rotation).InclusiveBetween(-360, 360);
    }
}

internal sealed class TextStyleValidator : AppValidator<TextStyle>
{
    public TextStyleValidator()
    {
        RuleFor(s => s.FontSizePt).InclusiveBetween(4, 200);
        RuleFor(s => s.Color).NotEmpty().Matches(DesignRules.HexColor()).WithMessage("Colour must be #RRGGBB or #RRGGBBAA.");
        RuleFor(s => s.Align).Must(a => DesignRules.Aligns.Contains(a)).WithMessage("Align must be left, center, right or justify.");
        RuleFor(s => s.LineHeight).InclusiveBetween(0.5m, 4m);
        RuleFor(s => s.LetterSpacing).InclusiveBetween(-5m, 50m);
        RuleFor(s => s.FontWeight).Must(w => w is >= 100 and <= 900 && w % 100 == 0).WithMessage("Font weight must be 100–900 in steps of 100.");
        RuleFor(s => s.TextTransform).Must(t => DesignRules.Transforms.Contains(t)).WithMessage("Text transform must be none, uppercase, lowercase or capitalize.");
    }
}

internal sealed class SaveTemplateDesignRequestValidator : AppValidator<SaveTemplateDesignRequest>
{
    public SaveTemplateDesignRequestValidator()
    {
        RuleFor(x => x.Design).NotNull().DependentRules(() =>
        {
            RuleFor(x => x.Design.SchemaVersion).Equal(TemplateDesign.CurrentSchemaVersion)
                .WithMessage($"Unsupported design schema version (expected {TemplateDesign.CurrentSchemaVersion}).");
            RuleFor(x => x.Design.Canvas.BackgroundColor).Matches(DesignRules.HexColor())
                .When(x => x.Design.Canvas?.BackgroundColor is not null).WithMessage("Canvas colour must be #RRGGBB.");
            RuleFor(x => x.Design.Pages).NotEmpty().WithMessage("A design needs at least one page.")
                .Must(p => p.Count <= DesignRules.MaxPages).WithMessage($"At most {DesignRules.MaxPages} pages are supported.")
                .Must(p => p.Select(x => x.Index).OrderBy(i => i).SequenceEqual(Enumerable.Range(0, p.Count)))
                .WithMessage("Page indexes must be 0, 1, 2… without gaps.");
            RuleFor(x => x.Design.Pages)
                .Must(pages => pages.SelectMany(p => p.Layers).Select(l => l.Id).Distinct().Count() == pages.Sum(p => p.Layers.Count))
                .WithMessage("Layer ids must be unique across the design.");

            RuleForEach(x => x.Design.Pages).Cascade(CascadeMode.Continue).ChildRules(page =>
            {
                page.RuleFor(p => p.Layers).Must(l => l.Count <= DesignRules.MaxLayersPerPage)
                    .WithMessage($"At most {DesignRules.MaxLayersPerPage} layers per page.");
                page.RuleFor(p => p.Background!.Fit).Must(f => DesignRules.Fits.Contains(f))
                    .When(p => p.Background is not null).WithMessage("Background fit must be cover, contain or stretch.");
                page.RuleFor(p => p.Background!.Color).Matches(DesignRules.HexColor())
                    .When(p => p.Background?.Color is not null).WithMessage("Background colour must be #RRGGBB.");

                page.RuleForEach(p => p.Layers).ChildRules(layer =>
                {
                    layer.RuleFor(l => l.Id).Matches(DesignRules.LayerId()).WithMessage("Layer id must be lowercase letters, digits, '-' or '_'.");
                    layer.RuleFor(l => l.Type).Must(t => DesignRules.LayerTypes.Contains(t)).WithMessage("Layer type must be text, image or shape.");
                    layer.RuleFor(l => l.Frame).NotNull().SetValidator(new LayerFrameValidator());
                    layer.RuleFor(l => l.Opacity).InclusiveBetween(0, 1);
                    layer.RuleFor(l => l.ZIndex).InclusiveBetween(0, 999);

                    layer.RuleFor(l => l.Text).NotEmpty().WithMessage("Text layers need text.").MaximumLength(500).When(l => l.Type == "text");
                    layer.RuleFor(l => l.Style).NotNull().SetValidator(new TextStyleValidator()!).When(l => l.Type == "text");
                    layer.RuleFor(l => l.AssetId).NotNull().When(l => l.Type == "image").WithMessage("Image layers need an assetId.");
                    layer.RuleFor(l => l.Shape).Must(s => s is not null && DesignRules.Shapes.Contains(s)).When(l => l.Type == "shape")
                        .WithMessage("Shape layers need shape: rect, ellipse or line.");
                    layer.RuleFor(l => l.Fill).NotEmpty().Matches(DesignRules.HexColor()).When(l => l.Type == "shape")
                        .WithMessage("Shape layers need a fill colour #RRGGBB.");
                });
            });
        });
    }
}

internal sealed class SaveTemplateRequestValidator : AppValidator<SaveTemplateRequest>
{
    public SaveTemplateRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required.")
            .Must(n => n.Trim().Length >= 3).WithMessage("Name must be at least 3 characters.")
            .MaximumLength(200);
        RuleFor(x => x.Slug).OptionalSlug();
        RuleFor(x => x.CategoryId).NotEmpty().WithMessage("Category is required.");
        RuleFor(x => x.Description).MaximumLength(4000);
        RuleFor(x => x.Tier).IsInEnum();
        RuleFor(x => x.WidthMm).InclusiveBetween(50, 1000).WithMessage("Width must be between 50 and 1000 mm.");
        RuleFor(x => x.HeightMm).InclusiveBetween(50, 1000).WithMessage("Height must be between 50 and 1000 mm.");
        RuleFor(x => x.Dpi).InclusiveBetween(150, 600).WithMessage("DPI must be between 150 and 600.");
        RuleFor(x => x.BleedMm).InclusiveBetween(0, 10).WithMessage("Bleed must be between 0 and 10 mm.");
        RuleFor(x => x.SupportedLanguages)
            .Must(l => l is null || l.Count <= 12).WithMessage("At most 12 languages.")
            .Must(l => l is null || l.All(code => DesignRules.LanguageCode().IsMatch(code))).WithMessage("Languages must be ISO 639 codes such as \"en\", \"hi\", \"ta\".");
        RuleFor(x => x.Tags)
            .Must(t => t is null || t.Count <= 20).WithMessage("At most 20 tags.")
            .Must(t => t is null || t.All(tag => !string.IsNullOrWhiteSpace(tag) && tag.Trim().Length <= 40)).WithMessage("Tags must be 1–40 characters.");
    }
}

internal sealed class SaveTemplateFieldsRequestValidator : AppValidator<SaveTemplateFieldsRequest>
{
    public SaveTemplateFieldsRequestValidator()
    {
        RuleFor(x => x.Fields).NotNull()
            .Must(f => f.Count <= DesignRules.MaxFields).WithMessage($"At most {DesignRules.MaxFields} fields.")
            .Must(f => f.Select(x => x.FieldKey).Distinct().Count() == f.Count).WithMessage("Field keys must be unique.");

        RuleForEach(x => x.Fields).Cascade(CascadeMode.Continue).ChildRules(field =>
        {
            field.RuleFor(f => f.FieldKey).Matches(DesignRules.FieldKey())
                .WithMessage("Field key must be lowercase snake_case, e.g. \"bride_name\".");
            field.RuleFor(f => f.Label).NotEmpty().MaximumLength(100);
            field.RuleFor(f => f.FieldType).IsInEnum();
            field.RuleFor(f => f.GroupName).MaximumLength(60);
            field.RuleFor(f => f.MaxLength).InclusiveBetween(1, 2000).When(f => f.MaxLength.HasValue);
            field.RuleFor(f => f.Placeholder).MaximumLength(200);
            field.RuleFor(f => f.DefaultValue).MaximumLength(1000);
            field.RuleFor(f => f.DisplayFormat).MaximumLength(60);
            field.RuleFor(f => f.PageIndex).InclusiveBetween(0, DesignRules.MaxPages - 1);
            field.RuleFor(f => f.ZIndex).InclusiveBetween(0, 999);
            field.RuleFor(f => f.Frame).NotNull().SetValidator(new LayerFrameValidator());
            field.RuleFor(f => f.Options).NotEmpty().When(f => f.FieldType == EditableFieldType.Select)
                .WithMessage("Select fields need at least one option.");
            field.RuleFor(f => f.Style).NotNull().When(f => f.FieldType != EditableFieldType.Image)
                .WithMessage("Text fields need a style (font size, colour, alignment).");
            field.RuleFor(f => f.Style!).SetValidator(new TextStyleValidator()).When(f => f.Style is not null);
            field.RuleFor(f => f.LabelTranslations)
                .Must(t => t is null || t.All(kv => DesignRules.LanguageCode().IsMatch(kv.Key) && kv.Value.Length is > 0 and <= 100))
                .WithMessage("Label translations must map language codes to labels of 1–100 characters.");
        });
    }
}

internal sealed class SaveTemplateFontsRequestValidator : AppValidator<SaveTemplateFontsRequest>
{
    public SaveTemplateFontsRequestValidator()
    {
        RuleFor(x => x.Fonts).NotNull()
            .Must(f => f.Count <= 20).WithMessage("At most 20 fonts per template.")
            .Must(f => f.Select(x => x.FontId).Distinct().Count() == f.Count).WithMessage("Each font may appear only once.")
            .Must(f => f.Count(x => x.IsDefault) <= 1).WithMessage("Only one font can be the default.");
        RuleForEach(x => x.Fonts).Cascade(CascadeMode.Continue).ChildRules(font =>
        {
            font.RuleFor(f => f.FontId).NotEmpty();
            font.RuleFor(f => f.Role).IsInEnum();
        });
    }
}

internal sealed class SaveTemplatePricingRequestValidator : AppValidator<SaveTemplatePricingRequest>
{
    public SaveTemplatePricingRequestValidator()
    {
        RuleFor(x => x.Prices).NotNull()
            .Must(p => p.Select(x => x.PackageId).Distinct().Count() == p.Count).WithMessage("Each package may appear only once.");
        RuleForEach(x => x.Prices).Cascade(CascadeMode.Continue).ChildRules(price =>
        {
            price.RuleFor(p => p.PackageId).NotEmpty();
            price.RuleFor(p => p.Price).InclusiveBetween(0, 1_000_000).PrecisionScale(12, 2, ignoreTrailingZeros: true);
            price.RuleFor(p => p.CompareAtPrice).GreaterThanOrEqualTo(p => p.Price).When(p => p.CompareAtPrice.HasValue)
                .WithMessage("The \"was\" price must be at least the actual price.");
        });
    }
}

internal sealed class SetTemplateStatusRequestValidator : AppValidator<SetTemplateStatusRequest>
{
    public SetTemplateStatusRequestValidator() => RuleFor(x => x.Status).IsInEnum();
}
