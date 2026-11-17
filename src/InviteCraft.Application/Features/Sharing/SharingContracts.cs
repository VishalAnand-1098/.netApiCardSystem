using InviteCraft.Application.Common.Models;
using InviteCraft.Application.Features.Invitations;
using InviteCraft.Application.Features.Templates;
using InviteCraft.Domain.Enums;

namespace InviteCraft.Application.Features.Sharing;

/// <summary>Bound from the "Sharing" section by the Infrastructure layer.</summary>
public sealed class SharingSettings
{
    /// <summary>
    /// Origin the share links are built on, e.g. "https://invitecraft.example". Empty → the API uses the
    /// origin of the current request (right in development behind the SPA proxy).
    /// </summary>
    public string PublicOrigin { get; set; } = string.Empty;

    /// <summary>false → no preview images are rendered (set automatically when rendering is disabled); the template thumbnail is used instead.</summary>
    public bool RenderImages { get; set; } = true;
}

public sealed record UpdateSharingRequest(InvitationVisibility Visibility);

/// <param name="ShareUrl">The link to send (the OG page; it forwards people to the viewing page).</param>
/// <param name="WhatsAppUrl">wa.me link with a pre-filled message.</param>
public sealed record InvitationSharingDto(
    InvitationVisibility Visibility,
    string? ShareCode,
    string? ShareUrl,
    string? WhatsAppUrl,
    /// <summary>true → the shared card carries the preview watermark (premium design not yet purchased).</summary>
    bool Watermarked,
    long ViewCount);

/// <summary>Everything the public viewing page needs to draw the card. No account data beyond the card itself.</summary>
public sealed record PublicInvitationDto(
    string ShareCode,
    string Title,
    string Language,
    bool Watermarked,
    /// <summary>Public preview image (also the OG image); the template thumbnail when no render exists.</summary>
    string? ImageUrl,
    IReadOnlyList<InvitationValueDto> Values,
    TemplateDetailDto Template);

public interface ISharingService
{
    Task<Result<InvitationSharingDto>> GetAsync(Guid userId, Guid invitationId, string origin, CancellationToken cancellationToken = default);

    /// <summary>Turns link sharing on or off. The first turn-on mints the share code; it stays stable afterwards.</summary>
    Task<Result<InvitationSharingDto>> UpdateAsync(Guid userId, Guid invitationId, UpdateSharingRequest request, string origin, CancellationToken cancellationToken = default);

    /// <summary>The public view. Counts the view and refreshes a stale preview image. Null result = 404 (unknown, private or expired).</summary>
    Task<Result<PublicInvitationDto>> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>What the OG page needs; null = 404. Never counts a view (crawlers fetch it).</summary>
    Task<SharePreview?> GetPreviewAsync(string slug, CancellationToken cancellationToken = default);
}

public sealed record SharePreview(string Slug, string Title, string TemplateName, string? ImageUrl);

public static class SharingErrors
{
    public static readonly Error NotFound = Error.NotFound("Share.NotFound", "This invitation is not shared (or the link is no longer active).");

    public static readonly Error PublicNotSupported = Error.Validation("Share.PublicNotSupported",
        "Public gallery listing is not available yet. Use \"Anyone with the link\".", "visibility");
}
