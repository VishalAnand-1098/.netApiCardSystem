using System.Net;
using InviteCraft.Application.Common.Models;
using InviteCraft.Application.Features.Invitations;
using InviteCraft.Application.Features.Templates;
using InviteCraft.Domain.Entities.Invitations;
using InviteCraft.Domain.Enums;

namespace InviteCraft.Application.Features.Rendering;

// ------------------------------------------------------------------ requests / responses

/// <param name="Dpi">JPG/PNG pixel density (96, 150 or 300, up to the package's limit). Omit for the highest allowed. Ignored for PDF.</param>
public sealed record CreateDownloadRequest(DownloadFormat Format, int? Dpi);

/// <summary>?invitationId=&amp;page=&amp;pageSize=</summary>
public sealed record DownloadQuery : PagedRequest
{
    public Guid? InvitationId { get; init; }
}

/// <param name="DownloadUrl">Short-lived signed link, present only when the file is <c>Ready</c> and not expired.</param>
public sealed record DownloadDto(
    Guid Id,
    Guid InvitationId,
    string InvitationTitle,
    DownloadFormat Format,
    int Dpi,
    bool HasWatermark,
    DownloadStatus Status,
    long? FileSizeBytes,
    string? Error,
    string? FileName,
    string? DownloadUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? ExpiresAt);

public sealed record DownloadQualityDto(int Dpi, int WidthPx, int HeightPx, string Label);

public sealed record DownloadFormatOptionDto(
    DownloadFormat Format,
    string Label,
    string Description,
    bool Available,
    /// <summary>Why it is not available (for the "unlock" prompt); null when available.</summary>
    string? Reason,
    IReadOnlyList<DownloadQualityDto> Qualities);

/// <summary>What the customer may download right now, so the UI can offer exactly that.</summary>
public sealed record DownloadOptionsDto(
    bool CanDownload,
    string? BlockedReason,
    string PackageCode,
    string PackageName,
    /// <summary>true → every export carries the "Preview" watermark (premium design not yet purchased).</summary>
    bool Watermark,
    bool IsPreviewOnly,
    int? MaxDownloads,
    int? RemainingDownloads,
    IReadOnlyList<DownloadFormatOptionDto> Formats);

/// <summary>Caller details recorded with a download for the audit trail.</summary>
public sealed record ClientInfo(IPAddress? IpAddress, string? UserAgent);

public sealed record DownloadFile(Stream Content, string ContentType, string FileName);

// ------------------------------------------------------------------ renderer boundary

/// <summary>One field's content as the renderer needs it (same shape the editor uses).</summary>
public sealed record RenderValue(string? Value, string? ImageUrl, FieldStyleOverride? Style, FieldPosition? Position);

public sealed record WatermarkSpec(string Text, decimal Opacity);

/// <param name="PageIndex">Page to draw for JPG/PNG; PDFs always contain every page.</param>
public sealed record CardRenderRequest(
    TemplateDetailDto Template,
    string Language,
    int PageIndex,
    IReadOnlyDictionary<string, RenderValue> Values,
    DownloadFormat Format,
    int Dpi,
    WatermarkSpec? Watermark);

public sealed record RenderedFile(byte[] Content, string ContentType, string Extension, int WidthPx, int HeightPx);

/// <summary>Draws a card exactly as the editor preview does and returns the encoded file.</summary>
public interface ICardRenderer
{
    Task<RenderedFile> RenderAsync(CardRenderRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Hand-off from the API to the background render workers (ids only; the database row is the job).</summary>
public interface IRenderQueue
{
    ValueTask EnqueueAsync(Guid downloadId, CancellationToken cancellationToken = default);
}

/// <summary>HMAC-signed, expiring download links so a plain &lt;a href&gt; works without exposing a bearer token.</summary>
public interface IDownloadLinkSigner
{
    string Sign(Guid downloadId, DateTimeOffset expiresAt);
    bool IsValid(Guid downloadId, long expiresUnixSeconds, string signature, DateTimeOffset now);
}

// ------------------------------------------------------------------ persistence

public interface IDownloadRepository : Common.Interfaces.IRepository<Download>
{
    /// <summary>Tracked. Null when missing or owned by someone else.</summary>
    Task<Download?> GetOwnedAsync(Guid userId, Guid id, CancellationToken cancellationToken = default);

    /// <summary>Tracked, with invitation (for the render worker). No ownership filter: internal use only.</summary>
    Task<Download?> GetForRenderAsync(Guid id, CancellationToken cancellationToken = default);

    Task<PagedResult<Download>> SearchOwnedAsync(Guid userId, DownloadQuery query, CancellationToken cancellationToken = default);

    /// <summary>Downloads that count against the package's allowance: not failed, not watermarked previews.</summary>
    Task<int> CountBillableAsync(Guid invitationId, CancellationToken cancellationToken = default);

    Task<int> CountInProgressAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Queued/Rendering rows older than <paramref name="olderThan"/> (left behind by a restart).</summary>
    Task<IReadOnlyList<Guid>> GetUnfinishedIdsAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default);
}
