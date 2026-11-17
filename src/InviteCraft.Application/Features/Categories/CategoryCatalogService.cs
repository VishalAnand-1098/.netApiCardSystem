using InviteCraft.Application.Common.Models;

namespace InviteCraft.Application.Features.Categories;

/// <summary>Read-only category browsing for the storefront. Inactive and deleted rows are invisible.</summary>
public interface ICategoryCatalogService
{
    Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(PublicCategoryQuery query, CancellationToken cancellationToken = default);
    Task<Result<CategoryDto>> GetCategoryAsync(string slug, CancellationToken cancellationToken = default);
    Task<Result<SubCategoryDetailDto>> GetSubCategoryAsync(string categorySlug, string subCategorySlug, CancellationToken cancellationToken = default);
}

internal sealed class CategoryCatalogService(ICategoryRepository categories) : ICategoryCatalogService
{
    public Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(PublicCategoryQuery query, CancellationToken cancellationToken = default) =>
        categories.GetPublicCatalogAsync(query.Kind, cancellationToken);

    public async Task<Result<CategoryDto>> GetCategoryAsync(string slug, CancellationToken cancellationToken = default)
    {
        var category = await categories.GetPublicBySlugAsync(Normalize(slug), cancellationToken);
        return category is null ? CategoryErrors.NotFound : category;
    }

    public async Task<Result<SubCategoryDetailDto>> GetSubCategoryAsync(
        string categorySlug, string subCategorySlug, CancellationToken cancellationToken = default)
    {
        var detail = await categories.GetPublicSubCategoryAsync(Normalize(categorySlug), Normalize(subCategorySlug), cancellationToken);
        return detail is null ? CategoryErrors.SubCategoryNotFound : detail;
    }

    private static string Normalize(string slug) => slug.Trim().ToLowerInvariant();
}
