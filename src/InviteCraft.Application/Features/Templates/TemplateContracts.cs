using InviteCraft.Application.Common.Models;
using InviteCraft.Domain.Enums;

namespace InviteCraft.Application.Features.Templates;

// ------------------------------------------------------------------ shared pieces

public sealed record CategoryRefDto(Guid Id, string Name, string Slug);

public sealed record TemplateAssetDto(
    Guid Id,
    TemplateAssetType AssetType,
    int PageIndex,
    int SortOrder,
    string? WebUrl,
    string? ThumbUrl,
    int? WidthPx,
    int? HeightPx,
    long SizeBytes,
    string MimeType);

public sealed record TemplateFieldDto(
    Guid Id,
    string FieldKey,
    string Label,
    IReadOnlyDictionary<string, string>? LabelTranslations,
    EditableFieldType FieldType,
    string? GroupName,
    bool IsRequired,
    int? MaxLength,
    string? Placeholder,
    string? DefaultValue,
    IReadOnlyList<string>? Options,
    string? DisplayFormat,
    int PageIndex,
    LayerFrame Frame,
    int ZIndex,
    TextStyle? Style,
    FieldPermissions Permissions,
    int SortOrder,
    bool IsActive);

public sealed record TemplateFontDto(
    Guid FontId,
    string FamilyName,
    string DisplayName,
    int Weight,
    string Style,
    IReadOnlyList<string> Scripts,
    string? Url,
    FontRole Role,
    bool IsDefault,
    int SortOrder);

public sealed record TemplatePriceDto(
    Guid PackageId,
    string PackageCode,
    string PackageName,
    string? PackageDescription,
    IReadOnlyList<string> Features,
    IReadOnlyList<string> AllowedFormats,
    int MaxDpi,
    bool RemovesWatermark,
    decimal Price,
    decimal? CompareAtPrice,
    string Currency,
    bool IsRecommended,
    bool IsActive);

// ------------------------------------------------------------------ storefront

/// <summary>Marketplace card.</summary>
public sealed record TemplateCardDto(
    Guid Id,
    string Code,
    string Name,
    string Slug,
    TemplateTier Tier,
    TemplateOrientation Orientation,
    decimal? StartingPrice,
    string? ThumbnailUrl,
    string? PreviewUrl,
    CategoryRefDto Category,
    CategoryRefDto? SubCategory,
    IReadOnlyList<string> SupportedLanguages,
    decimal AvgRating,
    int ReviewCount,
    bool IsFeatured,
    DateTimeOffset? PublishedAt);

/// <summary>Everything the template page and the invitation editor need to render the card.</summary>
public sealed record TemplateDetailDto(
    Guid Id,
    string Code,
    string Name,
    string Slug,
    string? Description,
    TemplateTier Tier,
    TemplateOrientation Orientation,
    decimal WidthMm,
    decimal HeightMm,
    int Dpi,
    decimal BleedMm,
    IReadOnlyList<string> SupportedLanguages,
    IReadOnlyList<string> Tags,
    int LayoutVersion,
    decimal? StartingPrice,
    decimal AvgRating,
    int ReviewCount,
    DateTimeOffset? PublishedAt,
    CategoryRefDto Category,
    CategoryRefDto? SubCategory,
    TemplateDesign Design,
    IReadOnlyList<TemplateFieldDto> Fields,
    IReadOnlyList<TemplateFontDto> Fonts,
    IReadOnlyList<TemplateAssetDto> Assets,
    IReadOnlyList<TemplatePriceDto> Pricing);

/// <summary>
/// ?search=&amp;categorySlug=&amp;subCategorySlug=&amp;tier=&amp;orientation=&amp;language=&amp;tag=&amp;minPrice=&amp;maxPrice=&amp;featured=
/// &amp;sortBy=recommended|popular|bestselling|newest|price|rating&amp;sortDir=
/// </summary>
public sealed record TemplateSearchQuery : PagedRequest
{
    public string? CategorySlug { get; init; }
    public string? SubCategorySlug { get; init; }
    public TemplateTier? Tier { get; init; }
    public TemplateOrientation? Orientation { get; init; }
    public string? Language { get; init; }
    public string? Tag { get; init; }
    public decimal? MinPrice { get; init; }
    public decimal? MaxPrice { get; init; }
    public bool? Featured { get; init; }
}

// ------------------------------------------------------------------ admin

public sealed record AdminTemplateListItemDto(
    Guid Id,
    string Code,
    string Name,
    string Slug,
    TemplateStatus Status,
    TemplateTier Tier,
    TemplateOrientation Orientation,
    string CategoryName,
    string? SubCategoryName,
    decimal? StartingPrice,
    string? ThumbnailUrl,
    bool IsFeatured,
    int FieldCount,
    long ViewCount,
    int SalesCount,
    DateTimeOffset? PublishedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Full admin view: storefront payload plus status, stats, inactive rows and publish readiness.</summary>
public sealed record AdminTemplateDto(
    TemplateDetailDto Template,
    TemplateStatus Status,
    bool IsFeatured,
    long ViewCount,
    int SalesCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<string> PublishIssues);

public sealed record AdminTemplateQuery : PagedRequest
{
    public Guid? CategoryId { get; init; }
    public Guid? SubCategoryId { get; init; }
    public TemplateStatus? Status { get; init; }
    public TemplateTier? Tier { get; init; }
}

/// <summary>Template metadata. Orientation is derived from the dimensions; the code (IC-10001…) is generated.</summary>
public sealed record SaveTemplateRequest(
    string Name,
    string? Slug,
    Guid CategoryId,
    Guid? SubCategoryId,
    string? Description,
    TemplateTier Tier,
    decimal WidthMm,
    decimal HeightMm,
    int Dpi,
    decimal BleedMm,
    IReadOnlyList<string>? SupportedLanguages,
    IReadOnlyList<string>? Tags,
    bool IsFeatured);

public sealed record SaveTemplateDesignRequest(TemplateDesign Design);

public sealed record TemplateFieldInput(
    string FieldKey,
    string Label,
    IReadOnlyDictionary<string, string>? LabelTranslations,
    EditableFieldType FieldType,
    string? GroupName,
    bool IsRequired,
    int? MaxLength,
    string? Placeholder,
    string? DefaultValue,
    IReadOnlyList<string>? Options,
    string? DisplayFormat,
    int PageIndex,
    LayerFrame Frame,
    int ZIndex,
    TextStyle? Style,
    FieldPermissions? Permissions,
    int SortOrder,
    bool IsActive = true);

/// <summary>Replaces the template's field set (matched by fieldKey). Removed fields already used by invitations are deactivated instead of deleted.</summary>
public sealed record SaveTemplateFieldsRequest(IReadOnlyList<TemplateFieldInput> Fields);

public sealed record TemplateFontInput(Guid FontId, FontRole Role, bool IsDefault, int SortOrder);

public sealed record SaveTemplateFontsRequest(IReadOnlyList<TemplateFontInput> Fonts);

public sealed record TemplatePriceInput(Guid PackageId, decimal Price, decimal? CompareAtPrice, bool IsActive = true);

public sealed record SaveTemplatePricingRequest(IReadOnlyList<TemplatePriceInput> Prices);

public sealed record SetTemplateStatusRequest(TemplateStatus Status);
