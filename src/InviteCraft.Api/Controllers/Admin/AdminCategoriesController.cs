using InviteCraft.Api.Contracts;
using InviteCraft.Api.Infrastructure;
using InviteCraft.Application.Features.Categories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InviteCraft.Api.Controllers.Admin;

/// <summary>Category management for the admin panel.</summary>
[Route(ApiRoutes.AdminV1 + "/categories")]
[Authorize(Policy = AuthPolicies.AdminOnly)]
public sealed class AdminCategoriesController(IAdminCategoryService categories) : ApiControllerBase
{
    /// <summary>Paged list. ?search=&amp;kind=&amp;isActive=&amp;sortBy=sortOrder|name|kind|createdAt|updatedAt&amp;sortDir=asc|desc&amp;page=&amp;pageSize=</summary>
    [HttpGet]
    [ProducesResponseType<ApiResponse<IReadOnlyList<AdminCategoryListItemDto>>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AdminCategoryListItemDto>>>> Search(
        [FromQuery] CategoryQuery query, CancellationToken cancellationToken) =>
        PagedResponse(await categories.SearchAsync(query, cancellationToken));

    /// <summary>One category with all of its subcategories (active and inactive).</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<ApiResponse<AdminCategoryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<AdminCategoryDto>>> Get(Guid id, CancellationToken cancellationToken) =>
        FromResult(await categories.GetAsync(id, cancellationToken));

    [HttpPost]
    [ProducesResponseType<ApiResponse<AdminCategoryDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<AdminCategoryDto>>> Create(SaveCategoryRequest request, CancellationToken cancellationToken) =>
        CreatedResult(await categories.CreateAsync(request, cancellationToken),
            c => $"/{ApiRoutes.AdminV1}/categories/{c.Id}", "Category created.");

    [HttpPut("{id:guid}")]
    [ProducesResponseType<ApiResponse<AdminCategoryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<AdminCategoryDto>>> Update(Guid id, SaveCategoryRequest request, CancellationToken cancellationToken) =>
        FromResult(await categories.UpdateAsync(id, request, cancellationToken), "Category updated.");

    /// <summary>Activate or deactivate. Inactive categories (and their templates) disappear from the storefront.</summary>
    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType<ApiResponse<AdminCategoryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<AdminCategoryDto>>> SetStatus(Guid id, SetActiveStatusRequest request, CancellationToken cancellationToken) =>
        FromResult(await categories.SetActiveAsync(id, request.IsActive, cancellationToken),
            request.IsActive ? "Category activated." : "Category deactivated.");

    /// <summary>Bulk update of display order (drag &amp; drop in the admin list).</summary>
    [HttpPut("sort-order")]
    public async Task<ActionResult> Reorder(ReorderRequest request, CancellationToken cancellationToken) =>
        FromResult(await categories.ReorderAsync(request, cancellationToken), "Display order saved.");

    /// <summary>Soft-deletes the category and its subcategories. Refused (409) while templates still use it.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        FromResult(await categories.DeleteAsync(id, cancellationToken), "Category deleted.");
}
