using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Application.Common.Models;
using InviteCraft.Domain.Entities.Templates;
using InviteCraft.Domain.Enums;

namespace InviteCraft.Application.Features.Templates;

/// <summary>Flat projection for marketplace/admin cards; URLs are added by the service.</summary>
public sealed record TemplateCardRow(
    Guid Id,
    string Code,
    string Name,
    string Slug,
    TemplateStatus Status,
    TemplateTier Tier,
    TemplateOrientation Orientation,
    decimal? StartingPrice,
    Guid? ThumbnailAssetId,
    Guid? PreviewAssetId,
    Guid CategoryId,
    string CategoryName,
    string CategorySlug,
    Guid? SubCategoryId,
    string? SubCategoryName,
    string? SubCategorySlug,
    List<string> SupportedLanguages,
    decimal AvgRating,
    int ReviewCount,
    bool IsFeatured,
    int FieldCount,
    long ViewCount,
    int SalesCount,
    DateTimeOffset? PublishedAt,
    DateTimeOffset UpdatedAt);

public interface ITemplateRepository : IRepository<Template>
{
    /// <summary>Published templates in active categories.</summary>
    Task<PagedResult<TemplateCardRow>> SearchPublishedAsync(TemplateSearchQuery query, CancellationToken cancellationToken = default);
    Task<PagedResult<TemplateCardRow>> SearchAdminAsync(AdminTemplateQuery query, CancellationToken cancellationToken = default);

    /// <summary>Template with category, subcategory, assets, fields, fonts (+Font) and pricing (+Package).</summary>
    Task<Template?> GetAggregateAsync(Guid id, bool track, CancellationToken cancellationToken = default);
    Task<Template?> GetPublishedAggregateBySlugAsync(string slug, CancellationToken cancellationToken = default);

    Task<bool> SlugExistsAsync(string slug, Guid? excludeId, CancellationToken cancellationToken = default);
    /// <summary>Next human-friendly code from the template_code_seq sequence, e.g. "IC-10001".</summary>
    Task<string> NextCodeAsync(CancellationToken cancellationToken = default);
    Task<int> CountInvitationsAsync(Guid templateId, CancellationToken cancellationToken = default);
    Task<IReadOnlySet<Guid>> GetFieldIdsWithValuesAsync(IReadOnlyCollection<Guid> fieldIds, CancellationToken cancellationToken = default);
    Task IncrementViewCountAsync(Guid templateId, CancellationToken cancellationToken = default);
    Task IncrementSalesCountAsync(Guid templateId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Package>> GetActivePackagesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Font>> GetActiveFontsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
}

public sealed record FontRow(
    Guid Id,
    string FamilyName,
    string DisplayName,
    int Weight,
    string Style,
    List<string> Scripts,
    string Woff2Key,
    bool HasTtf,
    string? License,
    bool IsActive,
    int TemplateCount,
    DateTimeOffset CreatedAt);

public interface IFontRepository : IRepository<Font>
{
    Task<PagedResult<FontRow>> SearchAsync(FontQuery query, CancellationToken cancellationToken = default);
    Task<FontRow?> GetRowAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> VariantExistsAsync(string familyName, int weight, string style, CancellationToken cancellationToken = default);
    /// <summary>Non-deleted templates whitelisting this font.</summary>
    Task<int> CountTemplatesUsingAsync(Guid fontId, CancellationToken cancellationToken = default);
}
