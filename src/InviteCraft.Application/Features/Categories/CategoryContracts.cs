using InviteCraft.Application.Common.Models;
using InviteCraft.Domain.Enums;

namespace InviteCraft.Application.Features.Categories;

// ------------------------------------------------------------------ public (storefront) DTOs

/// <summary>Active category with its active subcategories. TemplateCount = published templates.</summary>
public sealed record CategoryDto(
    Guid Id,
    string Name,
    string Slug,
    CategoryKind Kind,
    string? Description,
    string? Icon,
    string? ImageUrl,
    int SortOrder,
    string? SeoTitle,
    string? SeoDescription,
    int TemplateCount,
    IReadOnlyList<SubCategoryDto> SubCategories);

public sealed record SubCategoryDto(
    Guid Id,
    Guid CategoryId,
    string Name,
    string Slug,
    string? Description,
    string? ImageUrl,
    int SortOrder,
    int TemplateCount);

/// <summary>Subcategory page payload, with its parent for breadcrumbs.</summary>
public sealed record SubCategoryDetailDto(
    SubCategoryDto SubCategory,
    string CategoryName,
    string CategorySlug,
    CategoryKind CategoryKind);

public sealed record PublicCategoryQuery
{
    /// <summary>Optional filter, e.g. ?kind=Religion for "Browse by tradition".</summary>
    public CategoryKind? Kind { get; init; }
}

// ------------------------------------------------------------------ admin DTOs

public sealed record AdminCategoryListItemDto(
    Guid Id,
    string Name,
    string Slug,
    CategoryKind Kind,
    string? Icon,
    int SortOrder,
    bool IsActive,
    bool ShowOnHome,
    int SubCategoryCount,
    int TemplateCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record AdminCategoryDto(
    Guid Id,
    string Name,
    string Slug,
    CategoryKind Kind,
    string? Description,
    string? Icon,
    string? ImageUrl,
    int SortOrder,
    bool IsActive,
    bool ShowOnHome,
    string? SeoTitle,
    string? SeoDescription,
    int TemplateCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<AdminSubCategoryDto> SubCategories);

public sealed record AdminSubCategoryDto(
    Guid Id,
    Guid CategoryId,
    string CategoryName,
    string Name,
    string Slug,
    string? Description,
    string? ImageUrl,
    int SortOrder,
    bool IsActive,
    int TemplateCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

// ------------------------------------------------------------------ admin requests

/// <summary>Admin category list: ?search=&amp;kind=&amp;isActive=&amp;sortBy=sortOrder|name|kind|createdAt|updatedAt&amp;sortDir=</summary>
public sealed record CategoryQuery : PagedRequest
{
    public CategoryKind? Kind { get; init; }
    public bool? IsActive { get; init; }
}

/// <summary>Admin subcategory list: ?categoryId=&amp;search=&amp;isActive=&amp;sortBy=sortOrder|name|createdAt|updatedAt&amp;sortDir=</summary>
public sealed record SubCategoryQuery : PagedRequest
{
    public Guid? CategoryId { get; init; }
    public bool? IsActive { get; init; }
}

/// <summary>Create/update payload. When <see cref="Slug"/> is empty it is generated from the name.</summary>
public sealed record SaveCategoryRequest(
    string Name,
    string? Slug,
    CategoryKind Kind,
    string? Description,
    string? Icon,
    string? ImageUrl,
    int SortOrder,
    bool IsActive,
    bool ShowOnHome,
    string? SeoTitle,
    string? SeoDescription);

public sealed record SaveSubCategoryRequest(
    Guid CategoryId,
    string Name,
    string? Slug,
    string? Description,
    string? ImageUrl,
    int SortOrder,
    bool IsActive);

public sealed record SetActiveStatusRequest(bool IsActive);

public sealed record SortOrderItem(Guid Id, int SortOrder);

public sealed record ReorderRequest(IReadOnlyList<SortOrderItem> Items);
