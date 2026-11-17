using InviteCraft.Api.Contracts;
using InviteCraft.Application.Features.Templates;
using Microsoft.AspNetCore.Mvc;

namespace InviteCraft.Api.Controllers;

/// <summary>Public template marketplace.</summary>
[Route(ApiRoutes.V1 + "/templates")]
public sealed class TemplatesController(ITemplateCatalogService catalog) : ApiControllerBase
{
    /// <summary>
    /// Published templates. Filters: search, categorySlug, subCategorySlug, tier, orientation, language, tag,
    /// minPrice, maxPrice, featured. sortBy: recommended (default) | popular | bestselling | newest | price | rating.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<ApiResponse<IReadOnlyList<TemplateCardDto>>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<TemplateCardDto>>>> Search(
        [FromQuery] TemplateSearchQuery query, CancellationToken cancellationToken) =>
        PagedResponse(await catalog.SearchAsync(query, cancellationToken));

    /// <summary>Template page + editor payload: dimensions, design, fields, fonts, web assets and package prices.</summary>
    [HttpGet("{slug}")]
    [ProducesResponseType<ApiResponse<TemplateDetailDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<TemplateDetailDto>>> Get(string slug, CancellationToken cancellationToken) =>
        FromResult(await catalog.GetBySlugAsync(slug, cancellationToken));
}

/// <summary>Public pricing packages (pricing page, package picker).</summary>
[Route(ApiRoutes.V1 + "/packages")]
public sealed class PackagesController(ITemplateCatalogService catalog) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<ApiResponse<IReadOnlyList<PackageDto>>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PackageDto>>>> GetPackages(CancellationToken cancellationToken) =>
        OkResponse(await catalog.GetPackagesAsync(cancellationToken));
}
