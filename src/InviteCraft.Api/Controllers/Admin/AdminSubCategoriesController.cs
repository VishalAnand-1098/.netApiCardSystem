using InviteCraft.Api.Contracts;
using InviteCraft.Api.Infrastructure;
using InviteCraft.Application.Features.Categories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InviteCraft.Api.Controllers.Admin;

/// <summary>Subcategory management for the admin panel.</summary>
[Route(ApiRoutes.AdminV1 + "/subcategories")]
[Authorize(Policy = AuthPolicies.AdminOnly)]
public sealed class AdminSubCategoriesController(IAdminSubCategoryService subCategories) : ApiControllerBase
{
    /// <summary>Paged list. ?categoryId=&amp;search=&amp;isActive=&amp;sortBy=sortOrder|name|createdAt|updatedAt&amp;sortDir=&amp;page=&amp;pageSize=</summary>
    [HttpGet]
    [ProducesResponseType<ApiResponse<IReadOnlyList<AdminSubCategoryDto>>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AdminSubCategoryDto>>>> Search(
        [FromQuery] SubCategoryQuery query, CancellationToken cancellationToken) =>
        PagedResponse(await subCategories.SearchAsync(query, cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ApiResponse<AdminSubCategoryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<AdminSubCategoryDto>>> Get(Guid id, CancellationToken cancellationToken) =>
        FromResult(await subCategories.GetAsync(id, cancellationToken));

    [HttpPost]
    [ProducesResponseType<ApiResponse<AdminSubCategoryDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<AdminSubCategoryDto>>> Create(SaveSubCategoryRequest request, CancellationToken cancellationToken) =>
        CreatedResult(await subCategories.CreateAsync(request, cancellationToken),
            s => $"/{ApiRoutes.AdminV1}/subcategories/{s.Id}", "Subcategory created.");

    /// <summary>Update; changing categoryId moves the subcategory (refused while it has templates).</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<ApiResponse<AdminSubCategoryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<AdminSubCategoryDto>>> Update(Guid id, SaveSubCategoryRequest request, CancellationToken cancellationToken) =>
        FromResult(await subCategories.UpdateAsync(id, request, cancellationToken), "Subcategory updated.");

    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType<ApiResponse<AdminSubCategoryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<AdminSubCategoryDto>>> SetStatus(Guid id, SetActiveStatusRequest request, CancellationToken cancellationToken) =>
        FromResult(await subCategories.SetActiveAsync(id, request.IsActive, cancellationToken),
            request.IsActive ? "Subcategory activated." : "Subcategory deactivated.");

    [HttpPut("sort-order")]
    public async Task<ActionResult> Reorder(ReorderRequest request, CancellationToken cancellationToken) =>
        FromResult(await subCategories.ReorderAsync(request, cancellationToken), "Display order saved.");

    [HttpDelete("{id:guid}")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        FromResult(await subCategories.DeleteAsync(id, cancellationToken), "Subcategory deleted.");
}
