using InviteCraft.Application.Common.Models;
using InviteCraft.Application.Features.Categories;
using InviteCraft.Domain.Entities.Catalog;
using InviteCraft.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace InviteCraft.Infrastructure.Persistence.Repositories;

internal sealed class CategoryRepository(AppDbContext context) : Repository<Category>(context), ICategoryRepository
{
    // ------------------------------------------------------------------ storefront

    private IQueryable<CategoryDto> PublicProjection(IQueryable<Category> source) =>
        source
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new CategoryDto(
                c.Id, c.Name, c.Slug, c.Kind, c.Description, c.Icon, c.ImageUrl, c.SortOrder, c.SeoTitle, c.SeoDescription,
                c.Templates.Count(t => t.Status == TemplateStatus.Published),
                c.SubCategories
                    .Where(s => s.IsActive)
                    .OrderBy(s => s.SortOrder).ThenBy(s => s.Name)
                    .Select(s => new SubCategoryDto(
                        s.Id, s.CategoryId, s.Name, s.Slug, s.Description, s.ImageUrl, s.SortOrder,
                        s.Templates.Count(t => t.Status == TemplateStatus.Published)))
                    .ToList()));

    public async Task<IReadOnlyList<CategoryDto>> GetPublicCatalogAsync(CategoryKind? kind, CancellationToken cancellationToken = default)
    {
        var source = Set.AsNoTracking();
        if (kind is { } k)
            source = source.Where(c => c.Kind == k);
        return await PublicProjection(source).ToListAsync(cancellationToken);
    }

    public Task<CategoryDto?> GetPublicBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
        PublicProjection(Set.AsNoTracking().Where(c => c.Slug == slug)).FirstOrDefaultAsync(cancellationToken);

    public Task<SubCategoryDetailDto?> GetPublicSubCategoryAsync(
        string categorySlug, string subCategorySlug, CancellationToken cancellationToken = default) =>
        Context.SubCategories.AsNoTracking()
            .Where(s => s.Slug == subCategorySlug && s.IsActive && s.Category.Slug == categorySlug && s.Category.IsActive)
            .Select(s => new SubCategoryDetailDto(
                new SubCategoryDto(s.Id, s.CategoryId, s.Name, s.Slug, s.Description, s.ImageUrl, s.SortOrder,
                    s.Templates.Count(t => t.Status == TemplateStatus.Published)),
                s.Category.Name, s.Category.Slug, s.Category.Kind))
            .FirstOrDefaultAsync(cancellationToken);

    // ------------------------------------------------------------------ admin

    public Task<PagedResult<AdminCategoryListItemDto>> SearchAsync(CategoryQuery query, CancellationToken cancellationToken = default)
    {
        var source = Set.AsNoTracking();

        if (query.Kind is { } kind)
            source = source.Where(c => c.Kind == kind);
        if (query.IsActive is { } active)
            source = source.Where(c => c.IsActive == active);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = QueryExtensions.ContainsPattern(query.Search);
            source = source.Where(c =>
                EF.Functions.ILike(c.Name, pattern, QueryExtensions.LikeEscape) ||
                EF.Functions.ILike(c.Slug, pattern, QueryExtensions.LikeEscape));
        }

        // Whitelisted sort keys; anything else falls back to display order.
        var ordered = query.SortBy?.ToLowerInvariant() switch
        {
            "name" => source.OrderBy(c => c.Name, query.SortDir),
            "kind" => source.OrderBy(c => c.Kind, query.SortDir).ThenBy(c => c.SortOrder),
            "createdat" => source.OrderBy(c => c.CreatedAt, query.SortDir),
            "updatedat" => source.OrderBy(c => c.UpdatedAt, query.SortDir),
            _ => source.OrderBy(c => c.SortOrder, query.SortDir).ThenBy(c => c.Name),
        };

        return ordered
            .Select(c => new AdminCategoryListItemDto(
                c.Id, c.Name, c.Slug, c.Kind, c.Icon, c.SortOrder, c.IsActive, c.ShowOnHome,
                c.SubCategories.Count(), c.Templates.Count(), c.CreatedAt, c.UpdatedAt))
            .ToPagedResultAsync(query, cancellationToken);
    }

    public Task<AdminCategoryDto?> GetAdminDetailAsync(Guid id, CancellationToken cancellationToken = default) =>
        Set.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new AdminCategoryDto(
                c.Id, c.Name, c.Slug, c.Kind, c.Description, c.Icon, c.ImageUrl, c.SortOrder, c.IsActive, c.ShowOnHome,
                c.SeoTitle, c.SeoDescription, c.Templates.Count(), c.CreatedAt, c.UpdatedAt,
                c.SubCategories
                    .OrderBy(s => s.SortOrder).ThenBy(s => s.Name)
                    .Select(s => new AdminSubCategoryDto(
                        s.Id, s.CategoryId, c.Name, s.Name, s.Slug, s.Description, s.ImageUrl, s.SortOrder, s.IsActive,
                        s.Templates.Count(), s.CreatedAt, s.UpdatedAt))
                    .ToList()))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<Category?> GetWithSubCategoriesAsync(Guid id, CancellationToken cancellationToken = default) =>
        Set.Include(c => c.SubCategories).FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Category>> GetTrackedByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) =>
        await Set.Where(c => ids.Contains(c.Id)).ToListAsync(cancellationToken);

    public Task<bool> SlugExistsAsync(string slug, Guid? excludeId, CancellationToken cancellationToken = default) =>
        Set.AnyAsync(c => c.Slug == slug && (excludeId == null || c.Id != excludeId), cancellationToken);

    public Task<int> CountTemplatesAsync(Guid categoryId, CancellationToken cancellationToken = default) =>
        Context.Templates.CountAsync(t => t.CategoryId == categoryId, cancellationToken);
}

internal sealed class SubCategoryRepository(AppDbContext context) : Repository<SubCategory>(context), ISubCategoryRepository
{
    public Task<PagedResult<AdminSubCategoryDto>> SearchAsync(SubCategoryQuery query, CancellationToken cancellationToken = default)
    {
        var source = Set.AsNoTracking();

        if (query.CategoryId is { } categoryId)
            source = source.Where(s => s.CategoryId == categoryId);
        if (query.IsActive is { } active)
            source = source.Where(s => s.IsActive == active);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = QueryExtensions.ContainsPattern(query.Search);
            source = source.Where(s =>
                EF.Functions.ILike(s.Name, pattern, QueryExtensions.LikeEscape) ||
                EF.Functions.ILike(s.Slug, pattern, QueryExtensions.LikeEscape) ||
                EF.Functions.ILike(s.Category.Name, pattern, QueryExtensions.LikeEscape));
        }

        var ordered = query.SortBy?.ToLowerInvariant() switch
        {
            "name" => source.OrderBy(s => s.Name, query.SortDir),
            "createdat" => source.OrderBy(s => s.CreatedAt, query.SortDir),
            "updatedat" => source.OrderBy(s => s.UpdatedAt, query.SortDir),
            _ => source.OrderBy(s => s.Category.SortOrder).ThenBy(s => s.SortOrder).ThenBy(s => s.Name),
        };

        return ordered
            .Select(Projection)
            .ToPagedResultAsync(query, cancellationToken);
    }

    public Task<AdminSubCategoryDto?> GetAdminDetailAsync(Guid id, CancellationToken cancellationToken = default) =>
        Set.AsNoTracking().Where(s => s.Id == id).Select(Projection).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<SubCategory>> GetTrackedByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) =>
        await Set.Where(s => ids.Contains(s.Id)).ToListAsync(cancellationToken);

    public Task<bool> SlugExistsAsync(Guid categoryId, string slug, Guid? excludeId, CancellationToken cancellationToken = default) =>
        Set.AnyAsync(s => s.CategoryId == categoryId && s.Slug == slug && (excludeId == null || s.Id != excludeId), cancellationToken);

    public Task<int> CountTemplatesAsync(Guid subCategoryId, CancellationToken cancellationToken = default) =>
        Context.Templates.CountAsync(t => t.SubCategoryId == subCategoryId, cancellationToken);

    private static readonly System.Linq.Expressions.Expression<Func<SubCategory, AdminSubCategoryDto>> Projection = s =>
        new AdminSubCategoryDto(
            s.Id, s.CategoryId, s.Category.Name, s.Name, s.Slug, s.Description, s.ImageUrl, s.SortOrder, s.IsActive,
            s.Templates.Count(), s.CreatedAt, s.UpdatedAt);
}
