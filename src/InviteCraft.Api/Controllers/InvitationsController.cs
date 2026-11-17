using InviteCraft.Api.Contracts;
using InviteCraft.Api.Infrastructure;
using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Application.Common.Models;
using InviteCraft.Application.Features.Invitations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace InviteCraft.Api.Controllers;

/// <summary>The signed-in customer's invitations: create from a template, edit content, upload photos.</summary>
[Route(ApiRoutes.V1 + "/invitations")]
[Authorize]
public sealed class InvitationsController(IInvitationService invitations, ICurrentUser currentUser) : ApiControllerBase
{
    private Guid UserId => currentUser.UserId!.Value;

    /// <summary>My invitations, newest edit first. ?search=&amp;status=&amp;sortBy=updatedAt|title|createdAt&amp;page=&amp;pageSize=</summary>
    [HttpGet]
    [ProducesResponseType<ApiResponse<IReadOnlyList<InvitationListItemDto>>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<InvitationListItemDto>>>> List(
        [FromQuery] InvitationQuery query, CancellationToken cancellationToken) =>
        PagedResponse(await invitations.ListAsync(UserId, query, cancellationToken));

    /// <summary>Starts a draft from a published template, pre-filled with the template's sample content.</summary>
    [HttpPost]
    [ProducesResponseType<ApiResponse<InvitationEditorDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<InvitationEditorDto>>> Create(CreateInvitationRequest request, CancellationToken cancellationToken) =>
        CreatedResult(await invitations.CreateAsync(UserId, request, cancellationToken),
            e => $"/{ApiRoutes.V1}/invitations/{e.Invitation.Id}", "Your invitation has been started.");

    /// <summary>The invitation plus the template that drives the form and the live card.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<ApiResponse<InvitationEditorDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<InvitationEditorDto>>> Get(Guid id, CancellationToken cancellationToken) =>
        FromResult(await invitations.GetEditorAsync(UserId, id, cancellationToken));

    /// <summary>Saves the whole content (autosave). 409 when <c>version</c> is stale; 400 lists every invalid field.</summary>
    [HttpPut("{id:guid}/content")]
    [ProducesResponseType<ApiResponse<InvitationDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<InvitationDto>>> SaveContent(
        Guid id, SaveInvitationContentRequest request, CancellationToken cancellationToken) =>
        FromResult(await invitations.SaveContentAsync(UserId, id, request, cancellationToken));

    /// <summary>Uploads a photo for an Image field (JPEG/PNG/WebP ≤ 10 MB). Set the field's value to the returned assetId.</summary>
    [HttpPost("{id:guid}/images")]
    [EnableRateLimiting(RateLimitPolicies.Uploads)]
    [RequestSizeLimit(UploadLimits.MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadLimits.MaxRequestBytes)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<ApiResponse<InvitationImageDto>>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<InvitationImageDto>>> UploadImage(
        Guid id, [FromForm] UploadInvitationImageForm form, CancellationToken cancellationToken)
    {
        if (form.File is null)
            return ErrorResponse([Error.Validation("Invitation.FileRequired", "Choose an image to upload.", "file")]);

        await using var stream = form.File.OpenReadStream();
        var upload = new FileUpload(stream, form.File.FileName, form.File.ContentType, form.File.Length);
        return CreatedResult(await invitations.UploadImageAsync(UserId, id, form.FieldKey, upload, cancellationToken),
            image => image.Url, "Image uploaded.");
    }

    /// <summary>Copies an invitation (text, styling, positions — not photos) into a new draft titled "… (copy)".</summary>
    [HttpPost("{id:guid}/duplicate")]
    [ProducesResponseType<ApiResponse<InvitationEditorDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<InvitationEditorDto>>> Duplicate(Guid id, CancellationToken cancellationToken) =>
        CreatedResult(await invitations.DuplicateAsync(UserId, id, cancellationToken),
            e => $"/{ApiRoutes.V1}/invitations/{e.Invitation.Id}", "Invitation duplicated.");

    /// <summary>Deletes a draft (soft delete). Purchased invitations cannot be deleted.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        FromResult(await invitations.DeleteAsync(UserId, id, cancellationToken), "Invitation deleted.");
}
