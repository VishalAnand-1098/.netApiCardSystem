using InviteCraft.Api.Contracts;
using InviteCraft.Application.Features.Content;
using Microsoft.AspNetCore.Mvc;

namespace InviteCraft.Api.Controllers;

/// <summary>Festival collections ("Diwali Specials") shown on the homepage and collection pages.</summary>
[Route(ApiRoutes.V1 + "/collections")]
public sealed class CollectionsController(IContentService content) : ApiControllerBase
{
    /// <summary>Active collections inside their date window; each carries up to 4 preview templates.</summary>
    [HttpGet]
    [ProducesResponseType<ApiResponse<IReadOnlyList<CollectionDto>>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<CollectionDto>>>> GetCollections(CancellationToken cancellationToken) =>
        OkResponse(await content.GetCollectionsAsync(cancellationToken));

    [HttpGet("{slug}")]
    [ProducesResponseType<ApiResponse<CollectionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<CollectionDto>>> GetCollection(string slug, CancellationToken cancellationToken) =>
        FromResult(await content.GetCollectionAsync(slug, cancellationToken));
}

/// <summary>Featured customer testimonials.</summary>
[Route(ApiRoutes.V1 + "/testimonials")]
public sealed class TestimonialsController(IContentService content) : ApiControllerBase
{
    /// <summary>?limit=6 (1–12). Authors are abbreviated ("Priya S.").</summary>
    [HttpGet]
    [ProducesResponseType<ApiResponse<IReadOnlyList<TestimonialDto>>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<TestimonialDto>>>> GetTestimonials(
        [FromQuery] int limit = 6, CancellationToken cancellationToken = default) =>
        OkResponse(await content.GetTestimonialsAsync(limit, cancellationToken));
}

/// <summary>Public site information managed from the admin settings page.</summary>
[Route(ApiRoutes.V1 + "/site")]
public sealed class SiteController(IContentService content) : ApiControllerBase
{
    [HttpGet("contact")]
    [ProducesResponseType<ApiResponse<SiteContactDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<SiteContactDto>>> GetContact(CancellationToken cancellationToken) =>
        OkResponse(await content.GetContactAsync(cancellationToken));
}
