using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Application.Common.Models;
using InviteCraft.Domain.Entities.Catalog;

namespace InviteCraft.Application.Features.Categories;

public interface IAdminSubCategoryService
{
    Task<PagedResult<AdminSubCategoryDto>> SearchAsync(SubCategoryQuery query, CancellationToken cancellationToken = default);
    Task<Result<AdminSubCategoryDto>> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result<AdminSubCategoryDto>> CreateAsync(SaveSubCategoryRequest request, CancellationToken cancellationToken = default);
    Task<Result<AdminSubCategoryDto>> UpdateAsync(Guid id, SaveSubCategoryRequest request, CancellationToken cancellationToken = default);
    Task<Result<AdminSubCategoryDto>> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default);
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result> ReorderAsync(ReorderRequest request, CancellationToken cancellationToken = default);
}

internal sealed class AdminSubCategoryService(
    ISubCategoryRepository subCategories,
    ICategoryRepository categories,
    IUnitOfWork unitOfWork,
    IAuditLogger audit) : IAdminSubCategoryService
{
    public Task<PagedResult<AdminSubCategoryDto>> SearchAsync(SubCategoryQuery query, CancellationToken cancellationToken = default) =>
        subCategories.SearchAsync(query, cancellationToken);

    public async Task<Result<AdminSubCategoryDto>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var dto = await subCategories.GetAdminDetailAsync(id, cancellationToken);
        return dto is null ? CategoryErrors.SubCategoryNotFound : dto;
    }

    public async Task<Result<AdminSubCategoryDto>> CreateAsync(SaveSubCategoryRequest request, CancellationToken cancellationToken = default)
    {
        if (!await categories.ExistsAsync(c => c.Id == request.CategoryId, cancellationToken))
            return CategoryErrors.ParentNotFound;

        var slug = AdminCategoryService.ResolveSlug(request.Slug, request.Name);
        if (slug is null)
            return CategoryErrors.SlugRequired;
        if (await subCategories.SlugExistsAsync(request.CategoryId, slug, excludeId: null, cancellationToken))
            return CategoryErrors.SubCategorySlugTaken(slug);

        var subCategory = new SubCategory();
        Apply(subCategory, request, slug);
        await subCategories.AddAsync(subCategory, cancellationToken);

        audit.Log("SubCategoryCreated", nameof(SubCategory), subCategory.Id.ToString(),
            details: new { subCategory.CategoryId, subCategory.Name, subCategory.Slug });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await GetAsync(subCategory.Id, cancellationToken);
    }

    public async Task<Result<AdminSubCategoryDto>> UpdateAsync(Guid id, SaveSubCategoryRequest request, CancellationToken cancellationToken = default)
    {
        var subCategory = await subCategories.GetByIdAsync(id, cancellationToken);
        if (subCategory is null)
            return CategoryErrors.SubCategoryNotFound;

        var moving = subCategory.CategoryId != request.CategoryId;
        if (moving)
        {
            if (!await categories.ExistsAsync(c => c.Id == request.CategoryId, cancellationToken))
                return CategoryErrors.ParentNotFound;

            // Templates store both category and subcategory ids; moving would make them disagree.
            var templateCount = await subCategories.CountTemplatesAsync(id, cancellationToken);
            if (templateCount > 0)
                return CategoryErrors.CannotMoveWithTemplates(templateCount);
        }

        var slug = AdminCategoryService.ResolveSlug(request.Slug, request.Name);
        if (slug is null)
            return CategoryErrors.SlugRequired;
        if ((moving || slug != subCategory.Slug)
            && await subCategories.SlugExistsAsync(request.CategoryId, slug, id, cancellationToken))
            return CategoryErrors.SubCategorySlugTaken(slug);

        Apply(subCategory, request, slug);
        audit.Log("SubCategoryUpdated", nameof(SubCategory), id.ToString(),
            details: new { subCategory.CategoryId, subCategory.Name, subCategory.Slug, subCategory.IsActive });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<Result<AdminSubCategoryDto>> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default)
    {
        var subCategory = await subCategories.GetByIdAsync(id, cancellationToken);
        if (subCategory is null)
            return CategoryErrors.SubCategoryNotFound;

        if (subCategory.IsActive != isActive)
        {
            subCategory.IsActive = isActive;
            audit.Log(isActive ? "SubCategoryActivated" : "SubCategoryDeactivated", nameof(SubCategory), id.ToString());
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        return await GetAsync(id, cancellationToken);
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var subCategory = await subCategories.GetByIdAsync(id, cancellationToken);
        if (subCategory is null)
            return CategoryErrors.SubCategoryNotFound;

        var templateCount = await subCategories.CountTemplatesAsync(id, cancellationToken);
        if (templateCount > 0)
            return CategoryErrors.SubCategoryHasTemplates(templateCount);

        subCategories.Remove(subCategory); // soft delete
        audit.Log("SubCategoryDeleted", nameof(SubCategory), id.ToString(), details: new { subCategory.Name, subCategory.Slug });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> ReorderAsync(ReorderRequest request, CancellationToken cancellationToken = default)
    {
        var ids = request.Items.Select(i => i.Id).ToList();
        var tracked = await subCategories.GetTrackedByIdsAsync(ids, cancellationToken);
        if (tracked.Count != ids.Count)
            return CategoryErrors.ReorderItemsNotFound(ids.Count - tracked.Count);

        var order = request.Items.ToDictionary(i => i.Id, i => i.SortOrder);
        foreach (var subCategory in tracked)
            subCategory.SortOrder = order[subCategory.Id];

        audit.Log("SubCategoriesReordered", nameof(SubCategory), details: new { count = tracked.Count });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static void Apply(SubCategory subCategory, SaveSubCategoryRequest request, string slug)
    {
        subCategory.CategoryId = request.CategoryId;
        subCategory.Name = request.Name.Trim();
        subCategory.Slug = slug;
        subCategory.Description = AdminCategoryService.Clean(request.Description);
        subCategory.ImageUrl = AdminCategoryService.Clean(request.ImageUrl);
        subCategory.SortOrder = request.SortOrder;
        subCategory.IsActive = request.IsActive;
    }
}
