using InviteCraft.Api.Contracts;
using InviteCraft.Api.Infrastructure;
using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Application.Features.Rendering;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace InviteCraft.Api.Controllers;

/// <summary>
/// Exports of a customer's invitations as JPG/PNG/PDF. Creating one queues a render job (202); poll
/// <c>GET /downloads/{id}</c> until it is <c>Ready</c>, then fetch the signed <c>downloadUrl</c> it returns.
/// </summary>
[Route(ApiRoutes.V1)]
public sealed class DownloadsController(IDownloadService downloads, ICurrentUser currentUser) : ApiControllerBase
{
    private Guid UserId => currentUser.UserId!.Value;

    /// <summary>What this invitation may download now: formats, resolutions (with pixel sizes), watermark, remaining allowance.</summary>
    [HttpGet("invitations/{invitationId:guid}/downloads/options")]
    [Authorize]
    [ProducesResponseType<ApiResponse<DownloadOptionsDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<DownloadOptionsDto>>> Options(Guid invitationId, CancellationToken cancellationToken) =>
        FromResult(await downloads.GetOptionsAsync(UserId, invitationId, cancellationToken));

    /// <summary>
    /// Queues a render. 202 + the download in <c>Queued</c> state. 403 when the format/quality is not part of the package (the message says
    /// which package includes it), 400 when required details are missing, 409 when too many renders are already running.
    /// </summary>
    [HttpPost("invitations/{invitationId:guid}/downloads")]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Renders)]
    [ProducesResponseType<ApiResponse<DownloadDto>>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<DownloadDto>>> Create(
        Guid invitationId, CreateDownloadRequest request, CancellationToken cancellationToken)
    {
        var client = new ClientInfo(HttpContext.Connection.RemoteIpAddress, Request.Headers.UserAgent.ToString());
        var result = await downloads.CreateAsync(UserId, invitationId, request, client, cancellationToken);
        if (result.IsFailure)
            return ErrorResponse(result.Errors);

        return Accepted($"/{ApiRoutes.V1}/downloads/{result.Value.Id}", ApiResponse.Ok(result.Value, "Your file is being prepared.", traceId: TraceId));
    }

    /// <summary>My downloads, newest first. ?invitationId=&amp;page=&amp;pageSize=</summary>
    [HttpGet("downloads")]
    [Authorize]
    [ProducesResponseType<ApiResponse<IReadOnlyList<DownloadDto>>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<DownloadDto>>>> List([FromQuery] DownloadQuery query, CancellationToken cancellationToken) =>
        PagedResponse(await downloads.ListAsync(UserId, query, cancellationToken));

    /// <summary>Status of one download. When <c>Ready</c>, <c>downloadUrl</c> is a signed link valid for a few minutes.</summary>
    [HttpGet("downloads/{id:guid}")]
    [Authorize]
    [ProducesResponseType<ApiResponse<DownloadDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<DownloadDto>>> Get(Guid id, CancellationToken cancellationToken) =>
        FromResult(await downloads.GetAsync(UserId, id, cancellationToken));

    /// <summary>
    /// Streams the file. Authorised by the HMAC signature in the link (<c>exp</c>, <c>sig</c>) rather than a bearer token, so a plain
    /// <c>&lt;a href&gt;</c> works and the link stops working after it expires.
    /// </summary>
    [HttpGet("downloads/{id:guid}/file")]
    [AllowAnonymous]
    [ResponseCache(NoStore = true)]
    public async Task<IActionResult> GetFile(Guid id, [FromQuery] long exp, [FromQuery] string sig, CancellationToken cancellationToken)
    {
        var result = await downloads.OpenFileAsync(id, exp, sig ?? string.Empty, cancellationToken);
        if (result.IsFailure)
            return ErrorResponse(result.Errors);

        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
    }
}
