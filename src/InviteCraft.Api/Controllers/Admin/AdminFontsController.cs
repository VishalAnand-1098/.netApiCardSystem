using InviteCraft.Api.Contracts;
using InviteCraft.Api.Infrastructure;
using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Application.Common.Models;
using InviteCraft.Application.Features.Categories;
using InviteCraft.Application.Features.Templates;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InviteCraft.Api.Controllers.Admin;

/// <summary>Self-hosted font library used by templates.</summary>
[Route(ApiRoutes.AdminV1 + "/fonts")]
[Authorize(Policy = AuthPolicies.AdminOnly)]
public sealed class AdminFontsController(IAdminFontService fonts) : ApiControllerBase
{
    /// <summary>?search=&amp;script=devanagari&amp;isActive=&amp;page=&amp;pageSize=</summary>
    [HttpGet]
    [ProducesResponseType<ApiResponse<IReadOnlyList<FontDto>>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<FontDto>>>> Search([FromQuery] FontQuery query, CancellationToken cancellationToken) =>
        PagedResponse(await fonts.SearchAsync(query, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<FontDto>>> Get(Guid id, CancellationToken cancellationToken) =>
        FromResult(await fonts.GetAsync(id, cancellationToken));

    /// <summary>Upload a font: WOFF2 (required, served to browsers) and optional TTF/OTF (kept private for rendering).</summary>
    [HttpPost]
    [RequestSizeLimit(UploadLimits.MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadLimits.MaxRequestBytes)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<ApiResponse<FontDto>>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<FontDto>>> Upload([FromForm] UploadFontForm form, CancellationToken cancellationToken)
    {
        if (form.Woff2File is null)
            return ErrorResponse([Error.Validation("Font.FileRequired", "Choose a WOFF2 font file.", "woff2File")]);

        var metadata = new FontMetadata(form.FamilyName, form.DisplayName, form.Weight, form.Style,
            form.Scripts.Select(s => s.Trim().ToLowerInvariant()).ToList(), form.License);

        await using var woff2 = form.Woff2File.OpenReadStream();
        await using var ttf = form.TtfFile?.OpenReadStream();
        var result = await fonts.UploadAsync(metadata,
            new FileUpload(woff2, form.Woff2File.FileName, form.Woff2File.ContentType, form.Woff2File.Length),
            ttf is null ? null : new FileUpload(ttf, form.TtfFile!.FileName, form.TtfFile.ContentType, form.TtfFile.Length),
            cancellationToken);

        return CreatedResult(result, f => $"/{ApiRoutes.AdminV1}/fonts/{f.Id}", "Font uploaded.");
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ApiResponse<FontDto>>> Update(Guid id, UpdateFontRequest request, CancellationToken cancellationToken) =>
        FromResult(await fonts.UpdateAsync(id, request, cancellationToken), "Font updated.");

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<ApiResponse<FontDto>>> SetStatus(Guid id, SetActiveStatusRequest request, CancellationToken cancellationToken) =>
        FromResult(await fonts.SetActiveAsync(id, request.IsActive, cancellationToken),
            request.IsActive ? "Font activated." : "Font deactivated.");

    /// <summary>Refused (409) while templates use the font.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        FromResult(await fonts.DeleteAsync(id, cancellationToken), "Font deleted.");
}
