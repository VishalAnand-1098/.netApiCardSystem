using InviteCraft.Application.Common.Models;
using InviteCraft.Application.Features.Templates;
using InviteCraft.Domain.Entities.Templates;
using InviteCraft.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace InviteCraft.Infrastructure.Persistence.Repositories;

internal sealed class TemplateRepository(AppDbContext context) : Repository<Template>(context), ITemplateRepository
{
    internal static readonly System.Linq.Expressions.Expression<Func<Template, TemplateCardRow>> CardProjection = t =>
        new TemplateCardRow(
            t.Id, t.Code, t.Name, t.Slug, t.Status, t.Tier, t.Orientation, t.StartingPrice,
            t.Assets.Where(a => a.AssetType == TemplateAssetType.Thumbnail).OrderBy(a => a.SortOrder).Select(a => (Guid?)a.Id).FirstOrDefault(),
            t.Assets.Where(a => a.AssetType == TemplateAssetType.Preview).OrderBy(a => a.SortOrder).Select(a => (Guid?)a.Id).FirstOrDefault(),
            t.CategoryId, t.Category.Name, t.Category.Slug,
            t.SubCategoryId, t.SubCategory != null ? t.SubCategory.Name : null, t.SubCategory != null ? t.SubCategory.Slug : null,
            t.SupportedLanguages, t.AvgRating, t.ReviewCount, t.IsFeatured,
            t.EditableFields.Count(f => f.IsActive), t.ViewCount, t.SalesCount, t.PublishedAt, t.UpdatedAt);

    public Task<PagedResult<TemplateCardRow>> SearchPublishedAsync(TemplateSearchQuery query, CancellationToken cancellationToken = default)
    {
        var source = Set.AsNoTracking().Where(t =>
            t.Status == TemplateStatus.Published &&
            t.Category.IsActive &&
            (t.SubCategory == null || t.SubCategory.IsActive));

        if (!string.IsNullOrWhiteSpace(query.CategorySlug))
        {
            var slug = query.CategorySlug.Trim().ToLowerInvariant();
            source = source.Where(t => t.Category.Slug == slug);
        }
        if (!string.IsNullOrWhiteSpace(query.SubCategorySlug))
        {
            var slug = query.SubCategorySlug.Trim().ToLowerInvariant();
            source = source.Where(t => t.SubCategory != null && t.SubCategory.Slug == slug);
        }
        if (query.Tier is { } tier)
            source = source.Where(t => t.Tier == tier);
        if (query.Orientation is { } orientation)
            source = source.Where(t => t.Orientation == orientation);
        if (!string.IsNullOrWhiteSpace(query.Language))
        {
            var language = query.Language.Trim().ToLowerInvariant();
            source = source.Where(t => t.SupportedLanguages.Contains(language));
        }
        if (!string.IsNullOrWhiteSpace(query.Tag))
        {
            var tag = query.Tag.Trim().ToLowerInvariant();
            source = source.Where(t => t.Tags.Contains(tag));
        }
        if (query.MinPrice is { } min)
            source = source.Where(t => t.StartingPrice >= min);
        if (query.MaxPrice is { } max)
            source = source.Where(t => t.StartingPrice <= max);
        if (query.Featured is { } featured)
            source = source.Where(t => t.IsFeatured == featured);
        source = ApplySearch(source, query.Search);

        var ordered = query.SortBy?.ToLowerInvariant() switch
        {
            "popular" => source.OrderByDescending(t => t.ViewCount).ThenByDescending(t => t.PublishedAt),
            "bestselling" => source.OrderByDescending(t => t.SalesCount).ThenByDescending(t => t.ViewCount),
            "newest" => source.OrderByDescending(t => t.PublishedAt),
            "price" => source.OrderBy(t => t.StartingPrice, query.SortDir).ThenByDescending(t => t.PublishedAt),
            "rating" => source.OrderByDescending(t => t.AvgRating).ThenByDescending(t => t.ReviewCount),
            _ => source.OrderByDescending(t => t.IsFeatured).ThenByDescending(t => t.SalesCount).ThenByDescending(t => t.PublishedAt),
        };

        return ordered.Select(CardProjection).ToPagedResultAsync(query, cancellationToken);
    }

    public Task<PagedResult<TemplateCardRow>> SearchAdminAsync(AdminTemplateQuery query, CancellationToken cancellationToken = default)
    {
        var source = Set.AsNoTracking();
        if (query.CategoryId is { } categoryId)
            source = source.Where(t => t.CategoryId == categoryId);
        if (query.SubCategoryId is { } subId)
            source = source.Where(t => t.SubCategoryId == subId);
        if (query.Status is { } status)
            source = source.Where(t => t.Status == status);
        if (query.Tier is { } tier)
            source = source.Where(t => t.Tier == tier);
        source = ApplySearch(source, query.Search);

        var ordered = query.SortBy?.ToLowerInvariant() switch
        {
            "name" => source.OrderBy(t => t.Name, query.SortDir),
            "code" => source.OrderBy(t => t.Code, query.SortDir),
            "publishedat" => source.OrderBy(t => t.PublishedAt, query.SortDir),
            "sales" => source.OrderBy(t => t.SalesCount, query.SortDir),
            "views" => source.OrderBy(t => t.ViewCount, query.SortDir),
            "createdat" => source.OrderBy(t => t.CreatedAt, query.SortDir),
            _ => source.OrderByDescending(t => t.UpdatedAt),
        };

        return ordered.Select(CardProjection).ToPagedResultAsync(query, cancellationToken);
    }

    private static IQueryable<Template> ApplySearch(IQueryable<Template> source, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return source;

        var pattern = QueryExtensions.ContainsPattern(search);
        var term = search.Trim().ToLowerInvariant();
        return source.Where(t =>
            EF.Functions.ILike(t.Name, pattern, QueryExtensions.LikeEscape) ||
            EF.Functions.ILike(t.Code, pattern, QueryExtensions.LikeEscape) ||
            t.Tags.Contains(term));
    }

    private IQueryable<Template> Aggregate(bool track)
    {
        var query = Set
            .Include(t => t.Category)
            .Include(t => t.SubCategory)
            .Include(t => t.Assets)
            .Include(t => t.EditableFields)
            .Include(t => t.FontConfigurations).ThenInclude(c => c.Font)
            .Include(t => t.Pricing).ThenInclude(p => p.Package)
            .AsSplitQuery();
        return track ? query : query.AsNoTracking();
    }

    public Task<Template?> GetAggregateAsync(Guid id, bool track, CancellationToken cancellationToken = default) =>
        Aggregate(track).FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<Template?> GetPublishedAggregateBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
        Aggregate(track: false).FirstOrDefaultAsync(t =>
            t.Slug == slug && t.Status == TemplateStatus.Published && t.Category.IsActive &&
            (t.SubCategory == null || t.SubCategory.IsActive), cancellationToken);

    public Task<bool> SlugExistsAsync(string slug, Guid? excludeId, CancellationToken cancellationToken = default) =>
        Set.AnyAsync(t => t.Slug == slug && (excludeId == null || t.Id != excludeId), cancellationToken);

    public async Task<string> NextCodeAsync(CancellationToken cancellationToken = default)
    {
        var next = await Context.Database
            .SqlQueryRaw<long>($"SELECT nextval('{DbSequences.TemplateCode}') AS \"Value\"")
            .SingleAsync(cancellationToken);
        return $"IC-{next}";
    }

    public Task<int> CountInvitationsAsync(Guid templateId, CancellationToken cancellationToken = default) =>
        Context.Invitations.IgnoreQueryFilters().CountAsync(i => i.TemplateId == templateId, cancellationToken);

    public async Task<IReadOnlySet<Guid>> GetFieldIdsWithValuesAsync(IReadOnlyCollection<Guid> fieldIds, CancellationToken cancellationToken = default)
    {
        if (fieldIds.Count == 0)
            return new HashSet<Guid>();
        var used = await Context.InvitationFieldValues
            .Where(v => fieldIds.Contains(v.FieldId))
            .Select(v => v.FieldId)
            .Distinct()
            .ToListAsync(cancellationToken);
        return used.ToHashSet();
    }

    public Task IncrementViewCountAsync(Guid templateId, CancellationToken cancellationToken = default) =>
        Set.Where(t => t.Id == templateId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ViewCount, t => t.ViewCount + 1), cancellationToken);

    public Task IncrementSalesCountAsync(Guid templateId, CancellationToken cancellationToken = default) =>
        Set.Where(t => t.Id == templateId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.SalesCount, t => t.SalesCount + 1), cancellationToken);

    public async Task<IReadOnlyList<Package>> GetActivePackagesAsync(CancellationToken cancellationToken = default) =>
        await Context.Packages.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.SortOrder).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Font>> GetActiveFontsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) =>
        await Context.Fonts.AsNoTracking().Where(f => f.IsActive && ids.Contains(f.Id)).ToListAsync(cancellationToken);
}

internal sealed class FontRepository(AppDbContext context) : Repository<Font>(context), IFontRepository
{
    private IQueryable<FontRow> Rows(IQueryable<Font> source) =>
        source.Select(f => new FontRow(
            f.Id, f.FamilyName, f.DisplayName, f.Weight, f.Style, f.Scripts, f.Woff2Key, f.TtfKey != null, f.License, f.IsActive,
            Context.TemplateFontConfigurations.Count(c => c.FontId == f.Id && !c.Template.IsDeleted),
            f.CreatedAt));

    public Task<PagedResult<FontRow>> SearchAsync(FontQuery query, CancellationToken cancellationToken = default)
    {
        var source = Set.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Script))
        {
            var script = query.Script.Trim().ToLowerInvariant();
            source = source.Where(f => f.Scripts.Contains(script));
        }
        if (query.IsActive is { } active)
            source = source.Where(f => f.IsActive == active);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = QueryExtensions.ContainsPattern(query.Search);
            source = source.Where(f =>
                EF.Functions.ILike(f.FamilyName, pattern, QueryExtensions.LikeEscape) ||
                EF.Functions.ILike(f.DisplayName, pattern, QueryExtensions.LikeEscape));
        }

        var ordered = query.SortBy?.ToLowerInvariant() == "createdat"
            ? source.OrderBy(f => f.CreatedAt, query.SortDir)
            : source.OrderBy(f => f.DisplayName).ThenBy(f => f.Weight);

        return Rows(ordered).ToPagedResultAsync(query, cancellationToken);
    }

    public Task<FontRow?> GetRowAsync(Guid id, CancellationToken cancellationToken = default) =>
        Rows(Set.AsNoTracking().Where(f => f.Id == id)).FirstOrDefaultAsync(cancellationToken);

    public Task<bool> VariantExistsAsync(string familyName, int weight, string style, CancellationToken cancellationToken = default) =>
        Set.AnyAsync(f => f.FamilyName == familyName && f.Weight == weight && f.Style == style, cancellationToken);

    public Task<int> CountTemplatesUsingAsync(Guid fontId, CancellationToken cancellationToken = default) =>
        Context.TemplateFontConfigurations.CountAsync(c => c.FontId == fontId && !c.Template.IsDeleted, cancellationToken);
}
