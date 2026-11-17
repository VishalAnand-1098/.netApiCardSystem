using InviteCraft.Api.Contracts;
using InviteCraft.Api.Infrastructure;
using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Application.Common.Models;
using InviteCraft.Application.Features.Rendering;
using InviteCraft.Application.Features.Templates;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InviteCraft.Api.Controllers.Admin;

/// <summary>Template management: metadata, design, editable fields, fonts, pricing, assets and publishing.</summary>
[Route(ApiRoutes.AdminV1 + "/templates")]
[Authorize(Policy = AuthPolicies.AdminOnly)]
public sealed class AdminTemplatesController(
    IAdminTemplateService templates,
    ITemplateAssetService assets,
    ITemplateThumbnailService thumbnails) : ApiControllerBase
{
    /// <summary>?search=&amp;categoryId=&amp;subCategoryId=&amp;status=&amp;tier=&amp;sortBy=updatedAt|name|code|publishedAt|sales|views|createdAt&amp;sortDir=</summary>
    [HttpGet]
    [ProducesResponseType<ApiResponse<IReadOnlyList<AdminTemplateListItemDto>>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AdminTemplateListItemDto>>>> Search(
        [FromQuery] AdminTemplateQuery query, CancellationToken cancellationToken) =>
        PagedResponse(await templates.SearchAsync(query, cancellationToken));

    /// <summary>Full template incl. drafts, inactive fields/prices and a publish-readiness checklist.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<ApiResponse<AdminTemplateDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<AdminTemplateDto>>> Get(Guid id, CancellationToken cancellationToken) =>
        FromResult(await templates.GetAsync(id, cancellationToken));

    /// <summary>Preview payload (same shape the storefront editor receives) — works for drafts.</summary>
    [HttpGet("{id:guid}/preview")]
    [ProducesResponseType<ApiResponse<TemplateDetailDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<TemplateDetailDto>>> Preview(Guid id, CancellationToken cancellationToken)
    {
        var result = await templates.GetAsync(id, cancellationToken);
        return result.IsSuccess ? OkResponse(result.Value.Template) : ErrorResponse(result.Errors);
    }

    [HttpPost]
    [ProducesResponseType<ApiResponse<AdminTemplateDto>>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<AdminTemplateDto>>> Create(SaveTemplateRequest request, CancellationToken cancellationToken) =>
        CreatedResult(await templates.CreateAsync(request, cancellationToken),
            t => $"/{ApiRoutes.AdminV1}/templates/{t.Template.Id}", "Template created as a draft.");

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ApiResponse<AdminTemplateDto>>> Update(Guid id, SaveTemplateRequest request, CancellationToken cancellationToken) =>
        FromResult(await templates.UpdateAsync(id, request, cancellationToken), "Template details saved.");

    [HttpPut("{id:guid}/design")]
    public async Task<ActionResult<ApiResponse<AdminTemplateDto>>> SaveDesign(Guid id, SaveTemplateDesignRequest request, CancellationToken cancellationToken) =>
        FromResult(await templates.SaveDesignAsync(id, request, cancellationToken), "Design saved.");

    /// <summary>Replaces the editable field set (matched by fieldKey).</summary>
    [HttpPut("{id:guid}/fields")]
    public async Task<ActionResult<ApiResponse<AdminTemplateDto>>> SaveFields(Guid id, SaveTemplateFieldsRequest request, CancellationToken cancellationToken) =>
        FromResult(await templates.SaveFieldsAsync(id, request, cancellationToken), "Editable fields saved.");

    [HttpPut("{id:guid}/fonts")]
    public async Task<ActionResult<ApiResponse<AdminTemplateDto>>> SaveFonts(Guid id, SaveTemplateFontsRequest request, CancellationToken cancellationToken) =>
        FromResult(await templates.SaveFontsAsync(id, request, cancellationToken), "Fonts saved.");

    [HttpPut("{id:guid}/pricing")]
    public async Task<ActionResult<ApiResponse<AdminTemplateDto>>> SavePricing(Guid id, SaveTemplatePricingRequest request, CancellationToken cancellationToken) =>
        FromResult(await templates.SavePricingAsync(id, request, cancellationToken), "Pricing saved.");

    /// <summary>Draft | Published | Archived. Publishing runs the readiness checklist.</summary>
    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<ApiResponse<AdminTemplateDto>>> SetStatus(Guid id, SetTemplateStatusRequest request, CancellationToken cancellationToken) =>
        FromResult(await templates.SetStatusAsync(id, request.Status, cancellationToken), $"Template is now {request.Status}.");

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        FromResult(await templates.DeleteAsync(id, cancellationToken), "Template deleted.");

    /// <summary>Uploads an image (JPEG/PNG/WebP ≤ 15 MB). Returns web and thumbnail URLs; the original stays private.</summary>
    [HttpPost("{id:guid}/assets")]
    [RequestSizeLimit(UploadLimits.MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadLimits.MaxRequestBytes)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<ApiResponse<TemplateAssetDto>>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<TemplateAssetDto>>> UploadAsset(
        Guid id, [FromForm] UploadTemplateAssetForm form, CancellationToken cancellationToken)
    {
        if (form.File is null)
            return ErrorResponse([Error.Validation("TemplateAsset.FileRequired", "Choose an image to upload.", "file")]);

        await using var stream = form.File.OpenReadStream();
        var upload = new FileUpload(stream, form.File.FileName, form.File.ContentType, form.File.Length);
        return CreatedResult(await assets.UploadAsync(id, form.AssetType, form.PageIndex, upload, cancellationToken),
            a => a.WebUrl ?? $"/{ApiRoutes.AdminV1}/templates/{id}", "Image uploaded.");
    }

    /// <summary>
    /// Draws the template with its sample content (field defaults/placeholders) through the same renderer customers' downloads use and
    /// stores the result as the marketplace thumbnail. Replaces any existing thumbnail.
    /// </summary>
    [HttpPost("{id:guid}/thumbnail/generate")]
    [ProducesResponseType<ApiResponse<TemplateAssetDto>>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<TemplateAssetDto>>> GenerateThumbnail(Guid id, CancellationToken cancellationToken) =>
        CreatedResult(await thumbnails.GenerateAsync(id, cancellationToken),
            a => a.WebUrl ?? $"/{ApiRoutes.AdminV1}/templates/{id}", "Thumbnail generated.");

    [HttpDelete("{id:guid}/assets/{assetId:guid}")]
    public async Task<ActionResult> DeleteAsset(Guid id, Guid assetId, CancellationToken cancellationToken) =>
        FromResult(await assets.DeleteAsync(id, assetId, cancellationToken), "Image deleted.");
}
