using InviteCraft.Api.Contracts;
using InviteCraft.Application.Features.Categories;
using Microsoft.AspNetCore.Mvc;

namespace InviteCraft.Api.Controllers;

/// <summary>Public category browsing (storefront menus, "Browse by occasion / tradition", category pages).</summary>
[Route(ApiRoutes.V1 + "/categories")]
public sealed class CategoriesController(ICategoryCatalogService catalog) : ApiControllerBase
{
    /// <summary>All active categories with their active subcategories. Optional ?kind=Religion|Occasion|Festival|Other.</summary>
    [HttpGet]
    [ProducesResponseType<ApiResponse<IReadOnlyList<CategoryDto>>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<CategoryDto>>>> GetCategories(
        [FromQuery] PublicCategoryQuery query, CancellationToken cancellationToken) =>
        OkResponse(await catalog.GetCategoriesAsync(query, cancellationToken));

    [HttpGet("{slug}")]
    [ProducesResponseType<ApiResponse<CategoryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<CategoryDto>>> GetCategory(string slug, CancellationToken cancellationToken) =>
        FromResult(await catalog.GetCategoryAsync(slug, cancellationToken));

    [HttpGet("{categorySlug}/{subCategorySlug}")]
    [ProducesResponseType<ApiResponse<SubCategoryDetailDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<SubCategoryDetailDto>>> GetSubCategory(
        string categorySlug, string subCategorySlug, CancellationToken cancellationToken) =>
        FromResult(await catalog.GetSubCategoryAsync(categorySlug, subCategorySlug, cancellationToken));
}
