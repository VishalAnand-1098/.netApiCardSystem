using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Application.Common.Models;
using InviteCraft.Domain.Entities.Catalog;
using InviteCraft.Domain.Enums;

namespace InviteCraft.Application.Features.Categories;

/// <remarks>
/// Read methods return DTO projections (single SQL query, no tracking).
/// Methods returning entities return them tracked for modification.
/// Soft-deleted rows are never returned.
/// </remarks>
public interface ICategoryRepository : IRepository<Category>
{
    /// <summary>Active categories with active subcategories, ordered for display.</summary>
    Task<IReadOnlyList<CategoryDto>> GetPublicCatalogAsync(CategoryKind? kind, CancellationToken cancellationToken = default);
    Task<CategoryDto?> GetPublicBySlugAsync(string slug, CancellationToken cancellationToken = default);
    Task<SubCategoryDetailDto?> GetPublicSubCategoryAsync(string categorySlug, string subCategorySlug, CancellationToken cancellationToken = default);

    Task<PagedResult<AdminCategoryListItemDto>> SearchAsync(CategoryQuery query, CancellationToken cancellationToken = default);
    Task<AdminCategoryDto?> GetAdminDetailAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Category?> GetWithSubCategoriesAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Category>> GetTrackedByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
    Task<bool> SlugExistsAsync(string slug, Guid? excludeId, CancellationToken cancellationToken = default);

    /// <summary>Non-deleted templates in the category (any status).</summary>
    Task<int> CountTemplatesAsync(Guid categoryId, CancellationToken cancellationToken = default);
}

public interface ISubCategoryRepository : IRepository<SubCategory>
{
    Task<PagedResult<AdminSubCategoryDto>> SearchAsync(SubCategoryQuery query, CancellationToken cancellationToken = default);
    Task<AdminSubCategoryDto?> GetAdminDetailAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SubCategory>> GetTrackedByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
    Task<bool> SlugExistsAsync(Guid categoryId, string slug, Guid? excludeId, CancellationToken cancellationToken = default);

    /// <summary>Non-deleted templates in the subcategory (any status).</summary>
    Task<int> CountTemplatesAsync(Guid subCategoryId, CancellationToken cancellationToken = default);
}
