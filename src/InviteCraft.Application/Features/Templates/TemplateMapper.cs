using System.Text.Json;
using System.Text.Json.Serialization;
using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Domain.Entities.Templates;
using InviteCraft.Domain.Enums;

namespace InviteCraft.Application.Features.Templates;

/// <summary>Serialisation settings for the JSONB columns (camelCase, enums as strings).</summary>
public static class TemplateJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string? json) =>
        string.IsNullOrWhiteSpace(json) ? default : JsonSerializer.Deserialize<T>(json, Options);
}

/// <summary>Storage key conventions. Originals are private; derived web/thumb WebP files are public.</summary>
public static class TemplateStorageKeys
{
    public static string Original(Guid templateId, Guid assetId, string extension) =>
        $"{IFileStorage.PrivatePrefix}templates/{templateId}/{assetId}/original.{extension}";

    public static string Web(Guid templateId, Guid assetId) => $"{IFileStorage.PublicPrefix}templates/{templateId}/{assetId}/web.webp";
    public static string Thumb(Guid templateId, Guid assetId) => $"{IFileStorage.PublicPrefix}templates/{templateId}/{assetId}/thumb.webp";
    public static string PublicFolder(Guid templateId, Guid assetId) => $"{IFileStorage.PublicPrefix}templates/{templateId}/{assetId}/";
    public static string PrivateFolder(Guid templateId, Guid assetId) => $"{IFileStorage.PrivatePrefix}templates/{templateId}/{assetId}/";

    public static string FontWoff2(Guid fontId) => $"{IFileStorage.PublicPrefix}fonts/{fontId}.woff2";
    public static string FontTtf(Guid fontId) => $"{IFileStorage.PrivatePrefix}fonts/{fontId}.ttf";
}

public sealed class TemplateMapper(IFileStorage storage)
{
    public string? ThumbUrl(Guid templateId, Guid? assetId) =>
        assetId is { } id ? storage.GetPublicUrl(TemplateStorageKeys.Thumb(templateId, id)) : null;

    public string? WebUrl(Guid templateId, Guid? assetId) =>
        assetId is { } id ? storage.GetPublicUrl(TemplateStorageKeys.Web(templateId, id)) : null;

    public string? FontUrl(string woff2Key) => storage.GetPublicUrl(woff2Key);

    public TemplateCardDto ToCard(TemplateCardRow row) => new(
        row.Id, row.Code, row.Name, row.Slug, row.Tier, row.Orientation, row.StartingPrice,
        ThumbUrl(row.Id, row.ThumbnailAssetId),
        WebUrl(row.Id, row.PreviewAssetId ?? row.ThumbnailAssetId),
        new CategoryRefDto(row.CategoryId, row.CategoryName, row.CategorySlug),
        row.SubCategoryId is { } subId ? new CategoryRefDto(subId, row.SubCategoryName!, row.SubCategorySlug!) : null,
        row.SupportedLanguages, row.AvgRating, row.ReviewCount, row.IsFeatured, row.PublishedAt);

    public AdminTemplateListItemDto ToAdminListItem(TemplateCardRow row) => new(
        row.Id, row.Code, row.Name, row.Slug, row.Status, row.Tier, row.Orientation, row.CategoryName, row.SubCategoryName,
        row.StartingPrice, ThumbUrl(row.Id, row.ThumbnailAssetId), row.IsFeatured, row.FieldCount, row.ViewCount, row.SalesCount,
        row.PublishedAt, row.UpdatedAt);

    /// <param name="activePackages">Used to show the Free package on free templates that have no explicit price rows.</param>
    /// <param name="includeInactive">Admin view: also inactive fields and prices.</param>
    public TemplateDetailDto ToDetail(Template t, IReadOnlyList<Package> activePackages, bool includeInactive)
    {
        var fields = t.EditableFields
            .Where(f => includeInactive || f.IsActive)
            .OrderBy(f => f.PageIndex).ThenBy(f => f.SortOrder).ThenBy(f => f.FieldKey)
            .Select(ToField)
            .ToList();

        var fonts = t.FontConfigurations
            .Where(c => includeInactive || c.Font.IsActive)
            .OrderByDescending(c => c.IsDefault).ThenBy(c => c.SortOrder).ThenBy(c => c.Font.DisplayName)
            .Select(c => new TemplateFontDto(c.FontId, c.Font.FamilyName, c.Font.DisplayName, c.Font.Weight, c.Font.Style,
                c.Font.Scripts, FontUrl(c.Font.Woff2Key), c.Role, c.IsDefault, c.SortOrder))
            .ToList();

        var assets = t.Assets
            .OrderBy(a => a.AssetType).ThenBy(a => a.PageIndex).ThenBy(a => a.SortOrder)
            .Select(a => new TemplateAssetDto(a.Id, a.AssetType, a.PageIndex, a.SortOrder,
                WebUrl(t.Id, a.Id), ThumbUrl(t.Id, a.Id), a.WidthPx, a.HeightPx, a.SizeBytes, a.MimeType))
            .ToList();

        var pricing = t.Pricing
            .Where(p => includeInactive || (p.IsActive && p.Package.IsActive))
            .OrderBy(p => p.Package.SortOrder)
            .Select(p => ToPrice(p.Package, p.Price, p.CompareAtPrice, p.Currency, p.IsActive))
            .ToList();

        if (pricing.Count == 0 && t.Tier == TemplateTier.Free &&
            activePackages.FirstOrDefault(p => p.Code == Package.FreeCode) is { } free)
        {
            pricing.Add(ToPrice(free, 0m, null, Domain.Common.Currencies.Inr, true));
        }

        return new TemplateDetailDto(
            t.Id, t.Code, t.Name, t.Slug, t.Description, t.Tier, t.Orientation, t.WidthMm, t.HeightMm, t.Dpi, t.BleedMm,
            t.SupportedLanguages, t.Tags, t.LayoutVersion, t.StartingPrice, t.AvgRating, t.ReviewCount, t.PublishedAt,
            new CategoryRefDto(t.Category.Id, t.Category.Name, t.Category.Slug),
            t.SubCategory is { } sub ? new CategoryRefDto(sub.Id, sub.Name, sub.Slug) : null,
            TemplateJson.Deserialize<TemplateDesign>(t.Design) ?? TemplateDesign.Empty(),
            fields, fonts, assets, pricing);
    }

    public static TemplateFieldDto ToField(TemplateEditableField f) => new(
        f.Id, f.FieldKey, f.Label,
        TemplateJson.Deserialize<Dictionary<string, string>>(f.LabelTranslations),
        f.FieldType, f.GroupName, f.IsRequired, f.MaxLength, f.Placeholder, f.DefaultValue,
        TemplateJson.Deserialize<List<string>>(f.Options),
        f.DisplayFormat, f.PageIndex,
        new LayerFrame(f.X, f.Y, f.Width, f.Height, f.Rotation),
        f.ZIndex,
        TemplateJson.Deserialize<TextStyle>(f.Style),
        TemplateJson.Deserialize<FieldPermissions>(f.Permissions) ?? FieldPermissions.Default,
        f.SortOrder, f.IsActive);

    private static TemplatePriceDto ToPrice(Package package, decimal price, decimal? compareAt, string currency, bool isActive) => new(
        package.Id, package.Code, package.Name, package.Description,
        TemplateJson.Deserialize<List<string>>(package.Features) ?? [],
        package.AllowedFormats, package.MaxDpi, package.RemovesWatermark,
        price, compareAt, currency, package.IsRecommended, isActive);
}
