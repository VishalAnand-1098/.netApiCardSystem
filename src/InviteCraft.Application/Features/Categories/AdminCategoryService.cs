using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Application.Common.Models;
using InviteCraft.Application.Common.Text;
using InviteCraft.Domain.Entities.Catalog;

namespace InviteCraft.Application.Features.Categories;

public interface IAdminCategoryService
{
    Task<PagedResult<AdminCategoryListItemDto>> SearchAsync(CategoryQuery query, CancellationToken cancellationToken = default);
    Task<Result<AdminCategoryDto>> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result<AdminCategoryDto>> CreateAsync(SaveCategoryRequest request, CancellationToken cancellationToken = default);
    Task<Result<AdminCategoryDto>> UpdateAsync(Guid id, SaveCategoryRequest request, CancellationToken cancellationToken = default);
    Task<Result<AdminCategoryDto>> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default);
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result> ReorderAsync(ReorderRequest request, CancellationToken cancellationToken = default);
}

internal sealed class AdminCategoryService(
    ICategoryRepository categories,
    ISubCategoryRepository subCategories,
    IUnitOfWork unitOfWork,
    IAuditLogger audit) : IAdminCategoryService
{
    public Task<PagedResult<AdminCategoryListItemDto>> SearchAsync(CategoryQuery query, CancellationToken cancellationToken = default) =>
        categories.SearchAsync(query, cancellationToken);

    public async Task<Result<AdminCategoryDto>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var dto = await categories.GetAdminDetailAsync(id, cancellationToken);
        return dto is null ? CategoryErrors.NotFound : dto;
    }

    public async Task<Result<AdminCategoryDto>> CreateAsync(SaveCategoryRequest request, CancellationToken cancellationToken = default)
    {
        var slug = ResolveSlug(request.Slug, request.Name);
        if (slug is null)
            return CategoryErrors.SlugRequired;
        if (await categories.SlugExistsAsync(slug, excludeId: null, cancellationToken))
            return CategoryErrors.SlugTaken(slug);

        var category = new Category();
        Apply(category, request, slug);
        await categories.AddAsync(category, cancellationToken);

        audit.Log("CategoryCreated", nameof(Category), category.Id.ToString(), details: new { category.Name, category.Slug });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await GetAsync(category.Id, cancellationToken);
    }

    public async Task<Result<AdminCategoryDto>> UpdateAsync(Guid id, SaveCategoryRequest request, CancellationToken cancellationToken = default)
    {
        var category = await categories.GetByIdAsync(id, cancellationToken);
        if (category is null)
            return CategoryErrors.NotFound;

        var slug = ResolveSlug(request.Slug, request.Name);
        if (slug is null)
            return CategoryErrors.SlugRequired;
        if (slug != category.Slug && await categories.SlugExistsAsync(slug, id, cancellationToken))
            return CategoryErrors.SlugTaken(slug);

        var before = new { category.Name, category.Slug, category.IsActive };
        Apply(category, request, slug);

        audit.Log("CategoryUpdated", nameof(Category), id.ToString(), details: new { before, after = new { category.Name, category.Slug, category.IsActive } });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<Result<AdminCategoryDto>> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default)
    {
        var category = await categories.GetByIdAsync(id, cancellationToken);
        if (category is null)
            return CategoryErrors.NotFound;

        if (category.IsActive != isActive)
        {
            category.IsActive = isActive;
            audit.Log(isActive ? "CategoryActivated" : "CategoryDeactivated", nameof(Category), id.ToString());
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        return await GetAsync(id, cancellationToken);
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var category = await categories.GetWithSubCategoriesAsync(id, cancellationToken);
        if (category is null)
            return CategoryErrors.NotFound;

        var templateCount = await categories.CountTemplatesAsync(id, cancellationToken);
        if (templateCount > 0)
            return CategoryErrors.HasTemplates(templateCount);

        // Soft delete (the persistence layer converts Remove into is_deleted = true); children go with it.
        foreach (var sub in category.SubCategories)
            subCategories.Remove(sub);
        categories.Remove(category);

        audit.Log("CategoryDeleted", nameof(Category), id.ToString(),
            details: new { category.Name, category.Slug, subCategories = category.SubCategories.Count });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> ReorderAsync(ReorderRequest request, CancellationToken cancellationToken = default)
    {
        var ids = request.Items.Select(i => i.Id).ToList();
        var tracked = await categories.GetTrackedByIdsAsync(ids, cancellationToken);
        if (tracked.Count != ids.Count)
            return CategoryErrors.ReorderItemsNotFound(ids.Count - tracked.Count);

        var order = request.Items.ToDictionary(i => i.Id, i => i.SortOrder);
        foreach (var category in tracked)
            category.SortOrder = order[category.Id];

        audit.Log("CategoriesReordered", nameof(Category), details: new { count = tracked.Count });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>Explicit slug (normalised) or one generated from the name; null when neither yields a valid slug.</summary>
    internal static string? ResolveSlug(string? requested, string name)
    {
        var slug = string.IsNullOrWhiteSpace(requested) ? SlugGenerator.Generate(name) : requested.Trim().ToLowerInvariant();
        return SlugGenerator.IsValid(slug) ? slug : null;
    }

    private static void Apply(Category category, SaveCategoryRequest request, string slug)
    {
        category.Name = request.Name.Trim();
        category.Slug = slug;
        category.Kind = request.Kind;
        category.Description = Clean(request.Description);
        category.Icon = Clean(request.Icon);
        category.ImageUrl = Clean(request.ImageUrl);
        category.SortOrder = request.SortOrder;
        category.IsActive = request.IsActive;
        category.ShowOnHome = request.ShowOnHome;
        category.SeoTitle = Clean(request.SeoTitle);
        category.SeoDescription = Clean(request.SeoDescription);
    }

    internal static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
