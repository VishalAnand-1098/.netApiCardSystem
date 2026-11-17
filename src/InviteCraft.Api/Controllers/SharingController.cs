using System.Net;
using System.Text;
using InviteCraft.Api.Contracts;
using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Application.Features.Sharing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace InviteCraft.Api.Controllers;

/// <summary>Link sharing: the owner turns it on/off; the share link serves an OG page for WhatsApp previews and a public data endpoint for the viewing page.</summary>
[Route("")]
public sealed class SharingController(ISharingService sharing, ICurrentUser currentUser, IOptions<SharingSettings> options) : ApiControllerBase
{
    private Guid UserId => currentUser.UserId!.Value;

    /// <summary>The share origin: configured for production, the request's own origin in development (the SPA proxy preserves it).</summary>
    private string Origin => string.IsNullOrWhiteSpace(options.Value.PublicOrigin)
        ? $"{Request.Scheme}://{Request.Host}"
        : options.Value.PublicOrigin;

    [HttpGet(ApiRoutes.V1 + "/invitations/{id:guid}/sharing")]
    [Authorize]
    [ProducesResponseType<ApiResponse<InvitationSharingDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<InvitationSharingDto>>> Get(Guid id, CancellationToken cancellationToken) =>
        FromResult(await sharing.GetAsync(UserId, id, Origin, cancellationToken));

    /// <summary>Turns "anyone with the link" on or off. The link stays the same when sharing is turned off and on again.</summary>
    [HttpPut(ApiRoutes.V1 + "/invitations/{id:guid}/sharing")]
    [Authorize]
    [ProducesResponseType<ApiResponse<InvitationSharingDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<InvitationSharingDto>>> Update(Guid id, UpdateSharingRequest request, CancellationToken cancellationToken) =>
        FromResult(await sharing.UpdateAsync(UserId, id, request, Origin, cancellationToken),
            request.Visibility == Domain.Enums.InvitationVisibility.Private ? "Sharing turned off." : "Anyone with the link can now view this invitation.");

    /// <summary>Everything the public viewing page needs. No authentication; 404 for unknown, private or expired links.</summary>
    [HttpGet(ApiRoutes.V1 + "/share/{slug}")]
    [ProducesResponseType<ApiResponse<PublicInvitationDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<PublicInvitationDto>>> Public(string slug, CancellationToken cancellationToken) =>
        FromResult(await sharing.GetBySlugAsync(slug, cancellationToken));

    /// <summary>
    /// The link people actually send. Crawlers (WhatsApp, Telegram, …) read the OG tags; browsers are forwarded
    /// to the viewing page. Served by the API because the SPA cannot give crawlers meta tags.
    /// </summary>
    [HttpGet("s/{slug}")]
    public async Task<IActionResult> OgPage(string slug, CancellationToken cancellationToken)
    {
        var preview = await sharing.GetPreviewAsync(slug, cancellationToken);
        if (preview is null)
            return new ContentResult { StatusCode = 404, ContentType = "text/html; charset=utf-8", Content = SharePages.NotFound() };

        Response.Headers.CacheControl = "public, max-age=60";
        return Content(SharePages.Og(preview, Origin), "text/html; charset=utf-8", Encoding.UTF8);
    }
}

/// <summary>The tiny server-rendered pages behind /s/{slug}.</summary>
public static class SharePages
{
    public static string Og(SharePreview preview, string origin)
    {
        static string e(string value) => WebUtility.HtmlEncode(value);
        origin = origin.TrimEnd('/');

        var title = $"{preview.Title} · InviteCraft";
        var description = $"You're invited! Open the link to see this {preview.TemplateName} invitation.";
        var pageUrl = $"{origin}/s/{Uri.EscapeDataString(preview.Slug)}";
        var viewUrl = $"{origin}/i/{Uri.EscapeDataString(preview.Slug)}";
        var image = preview.ImageUrl is null ? null : preview.ImageUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? preview.ImageUrl : origin + preview.ImageUrl;
        var imageTags = image is null
            ? string.Empty
            : $"""
               <meta property="og:image" content="{e(image)}">
               <meta name="twitter:image" content="{e(image)}">
               """;

        return $"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <title>{e(title)}</title>
              <meta name="robots" content="noindex">
              <meta property="og:type" content="website">
              <meta property="og:site_name" content="InviteCraft">
              <meta property="og:title" content="{e(title)}">
              <meta property="og:description" content="{e(description)}">
              <meta property="og:url" content="{e(pageUrl)}">
              <meta name="twitter:card" content="summary_large_image">
              <meta name="twitter:title" content="{e(title)}">
              <meta name="twitter:description" content="{e(description)}">
              {imageTags}
              <meta http-equiv="refresh" content="0;url={e(viewUrl)}">
            </head>
            <body style="font-family:system-ui,sans-serif;text-align:center;padding:48px 16px;background:#fbf7f0;color:#14213d">
              <p>Taking you to the invitation…</p>
              <p><a href="{e(viewUrl)}">Open "{e(preview.Title)}"</a></p>
            </body>
            </html>
            """;
    }

    public static string NotFound() => """
        <!doctype html>
        <html lang="en">
        <head><meta charset="utf-8"><title>Invitation not found · InviteCraft</title><meta name="robots" content="noindex"></head>
        <body style="font-family:system-ui,sans-serif;text-align:center;padding:48px 16px;background:#fbf7f0;color:#14213d">
          <h1 style="font-size:1.4rem">This invitation link is no longer active</h1>
          <p>The person who shared it may have turned sharing off.</p>
          <p><a href="/">Create your own at InviteCraft</a></p>
        </body>
        </html>
        """;
}
