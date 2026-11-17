using System.Text;
using System.Text.Json;
using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Application.Common.Models;
using InviteCraft.Application.Features.Content;
using InviteCraft.Application.Features.Invitations;
using InviteCraft.Application.Features.Templates;
using InviteCraft.Domain.Entities.Invitations;
using InviteCraft.Domain.Entities.Templates;
using InviteCraft.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace InviteCraft.Application.Features.Rendering;

public interface IDownloadService
{
    /// <summary>What this invitation may export (formats, resolutions, watermark, remaining allowance).</summary>
    Task<Result<DownloadOptionsDto>> GetOptionsAsync(Guid userId, Guid invitationId, CancellationToken cancellationToken = default);

    /// <summary>Validates entitlement, records a Queued download and hands it to the render workers.</summary>
    Task<Result<DownloadDto>> CreateAsync(Guid userId, Guid invitationId, CreateDownloadRequest request, ClientInfo client, CancellationToken cancellationToken = default);

    Task<Result<DownloadDto>> GetAsync(Guid userId, Guid id, CancellationToken cancellationToken = default);
    Task<PagedResult<DownloadDto>> ListAsync(Guid userId, DownloadQuery query, CancellationToken cancellationToken = default);

    /// <summary>Opens the file behind a signed link (no login needed: the signature proves the owner asked for it).</summary>
    Task<Result<DownloadFile>> OpenFileAsync(Guid id, long expiresUnixSeconds, string signature, CancellationToken cancellationToken = default);
}

internal sealed class DownloadService(
    IDownloadRepository downloads,
    IInvitationRepository invitations,
    ITemplateRepository templates,
    IContentRepository content,
    IUnitOfWork unitOfWork,
    IRenderQueue queue,
    IDownloadLinkSigner signer,
    IFileStorage storage,
    IAuditLogger audit,
    TimeProvider timeProvider,
    ILogger<DownloadService> logger) : IDownloadService
{
    private const int DefaultLinkMinutes = 15;

    // ------------------------------------------------------------------ options

    public async Task<Result<DownloadOptionsDto>> GetOptionsAsync(Guid userId, Guid invitationId, CancellationToken cancellationToken = default)
    {
        var invitation = await invitations.GetOwnedAsync(userId, invitationId, includeValues: true, cancellationToken);
        if (invitation is null)
            return InvitationErrors.NotFound;

        var template = await templates.GetAggregateAsync(invitation.TemplateId, track: false, cancellationToken);
        if (template is null)
            return TemplateErrors.NotFound;

        var packages = await templates.GetActivePackagesAsync(cancellationToken);
        var entitlement = DownloadPolicy.Resolve(invitation, template, packages);
        var used = entitlement.MaxDownloads is null || entitlement.Watermark ? 0 : await downloads.CountBillableAsync(invitationId, cancellationToken);
        var remaining = entitlement.MaxDownloads is { } max && !entitlement.Watermark ? Math.Max(0, max - used) : (int?)null;

        var complete = RenderValueMapper.IsComplete(invitation, template);
        var blocked = !complete ? DownloadErrors.NotReady.Message
            : remaining == 0 ? DownloadErrors.AllowanceUsed(entitlement.MaxDownloads!.Value).Message
            : null;

        var formats = Enum.GetValues<DownloadFormat>()
            .Select(format => BuildFormatOption(format, entitlement, packages, template))
            .ToList();

        return new DownloadOptionsDto(blocked is null, blocked, entitlement.PackageCode, entitlement.PackageName,
            entitlement.Watermark, entitlement.PreviewOnly, entitlement.MaxDownloads, remaining, formats);
    }

    private static DownloadFormatOptionDto BuildFormatOption(
        DownloadFormat format, DownloadEntitlement entitlement, IReadOnlyList<Package> packages, Template template)
    {
        var (label, description) = format switch
        {
            DownloadFormat.Jpg => ("JPG", "Small and universal — ideal for WhatsApp and social media."),
            DownloadFormat.Png => ("PNG", "Sharp edges and lossless colour — ideal for screens."),
            DownloadFormat.Pdf => ("PDF", "Crisp vector text — ideal for email and home or office printing."),
            _ => ("Print-ready PDF", "Adds 3 mm bleed and crop marks for professional print shops."),
        };

        var available = entitlement.Formats.Contains(format);
        string? reason = null;
        if (!available)
        {
            var includedIn = packages.Where(p => p.AllowedFormats.Contains(format.ToString())).OrderBy(p => p.SortOrder).FirstOrDefault();
            reason = entitlement.PreviewOnly
                ? "Unlock this design to download it without the watermark."
                : includedIn is null ? "Not available for this invitation." : $"Included from the {includedIn.Name} package.";
        }

        IReadOnlyList<DownloadQualityDto> qualities;
        if (!available)
            qualities = [];
        else if (DownloadPolicy.IsImage(format))
            qualities = DownloadPolicy.AllowedDpis(entitlement)
                .Select(dpi => new DownloadQualityDto(dpi,
                    DownloadPolicy.PixelsFor(template.WidthMm, dpi), DownloadPolicy.PixelsFor(template.HeightMm, dpi), DownloadPolicy.QualityLabel(dpi)))
                .ToList();
        else
            qualities = [new DownloadQualityDto(Math.Min(DownloadPolicy.PdfDpi, entitlement.MaxDpi), 0, 0, "Vector")];

        return new DownloadFormatOptionDto(format, label, description, available, reason, qualities);
    }

    // ------------------------------------------------------------------ create

    public async Task<Result<DownloadDto>> CreateAsync(
        Guid userId, Guid invitationId, CreateDownloadRequest request, ClientInfo client, CancellationToken cancellationToken = default)
    {
        var invitation = await invitations.GetOwnedAsync(userId, invitationId, includeValues: true, cancellationToken);
        if (invitation is null)
            return InvitationErrors.NotFound;

        var template = await templates.GetAggregateAsync(invitation.TemplateId, track: false, cancellationToken);
        if (template is null)
            return TemplateErrors.NotFound;

        if (!RenderValueMapper.IsComplete(invitation, template))
            return DownloadErrors.NotReady;

        var packages = await templates.GetActivePackagesAsync(cancellationToken);
        var entitlement = DownloadPolicy.Resolve(invitation, template, packages);

        if (!entitlement.Formats.Contains(request.Format))
        {
            var option = BuildFormatOption(request.Format, entitlement, packages, template);
            return DownloadErrors.FormatNotAvailable(request.Format, option.Reason ?? "This format is not available.");
        }

        int dpi;
        if (DownloadPolicy.IsImage(request.Format))
        {
            var allowed = DownloadPolicy.AllowedDpis(entitlement);
            dpi = request.Dpi ?? allowed.LastOrDefault();
            if (!allowed.Contains(dpi))
                return DownloadErrors.QualityNotAvailable(dpi, entitlement.MaxDpi);
        }
        else
        {
            dpi = Math.Min(DownloadPolicy.PdfDpi, entitlement.MaxDpi);
        }

        if (entitlement.MaxDownloads is { } max && !entitlement.Watermark &&
            await downloads.CountBillableAsync(invitationId, cancellationToken) >= max)
            return DownloadErrors.AllowanceUsed(max);

        if (await downloads.CountInProgressAsync(userId, cancellationToken) >= DownloadPolicy.MaxInProgressPerUser)
            return DownloadErrors.TooManyInProgress;

        var now = timeProvider.GetUtcNow();
        var download = new Download
        {
            UserId = userId,
            InvitationId = invitationId,
            Format = request.Format,
            Dpi = dpi,
            HasWatermark = entitlement.Watermark,
            Status = DownloadStatus.Queued,
            IpAddress = client.IpAddress,
            UserAgent = client.UserAgent is { Length: > 400 } ua ? ua[..400] : client.UserAgent,
            ExpiresAt = now.AddDays(DownloadPolicy.FileRetentionDays),
        };

        await downloads.AddAsync(download, cancellationToken);
        audit.Log("DownloadRequested", nameof(Download), download.Id.ToString(), userId,
            new { invitationId, format = request.Format, dpi, watermark = entitlement.Watermark, package = entitlement.PackageCode });
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await queue.EnqueueAsync(download.Id, cancellationToken);
        logger.LogInformation("Download {DownloadId} queued: {Format} {Dpi} dpi for invitation {InvitationId}", download.Id, request.Format, dpi, invitationId);

        return await ToDtoAsync(download, invitation.Title, cancellationToken);
    }

    // ------------------------------------------------------------------ read

    public async Task<Result<DownloadDto>> GetAsync(Guid userId, Guid id, CancellationToken cancellationToken = default)
    {
        var download = await downloads.GetOwnedAsync(userId, id, cancellationToken);
        return download is null ? DownloadErrors.NotFound : await ToDtoAsync(download, download.Invitation.Title, cancellationToken);
    }

    public async Task<PagedResult<DownloadDto>> ListAsync(Guid userId, DownloadQuery query, CancellationToken cancellationToken = default)
    {
        var page = await downloads.SearchOwnedAsync(userId, query, cancellationToken);
        var minutes = await LinkMinutesAsync(cancellationToken);
        return new PagedResult<DownloadDto>(
            page.Items.Select(d => ToDto(d, d.Invitation.Title, minutes)).ToList(), page.Page, page.PageSize, page.TotalCount);
    }

    // ------------------------------------------------------------------ file

    public async Task<Result<DownloadFile>> OpenFileAsync(Guid id, long expiresUnixSeconds, string signature, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        if (!signer.IsValid(id, expiresUnixSeconds, signature, now))
            return DownloadErrors.LinkInvalid;

        var download = await downloads.GetForRenderAsync(id, cancellationToken);
        if (download is null || download.Status != DownloadStatus.Ready || download.StorageKey is null)
            return DownloadErrors.NotFound;
        if (download.ExpiresAt is { } expires && expires < now)
            return DownloadErrors.Expired;

        var stream = await storage.OpenReadAsync(download.StorageKey, cancellationToken);
        if (stream is null)
            return DownloadErrors.FileMissing;

        audit.Log("DownloadFetched", nameof(Download), id.ToString(), download.UserId, new { download.Format, download.HasWatermark });
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var (mime, extension) = DownloadPolicy.FileType(download.Format);
        return new DownloadFile(stream, mime, $"{FileNames.Slug(download.Invitation.Title)}{(download.HasWatermark ? "-preview" : string.Empty)}.{extension}");
    }

    // ------------------------------------------------------------------ mapping

    private async Task<DownloadDto> ToDtoAsync(Download download, string title, CancellationToken cancellationToken) =>
        ToDto(download, title, await LinkMinutesAsync(cancellationToken));

    private DownloadDto ToDto(Download d, string title, int linkMinutes)
    {
        var now = timeProvider.GetUtcNow();
        var expired = d.Status == DownloadStatus.Ready && d.ExpiresAt is { } e && e < now;
        var status = expired ? DownloadStatus.Expired : d.Status;
        var (_, extension) = DownloadPolicy.FileType(d.Format);

        string? url = null;
        string? fileName = null;
        if (status == DownloadStatus.Ready)
        {
            var linkExpires = now.AddMinutes(linkMinutes);
            url = $"/api/v1/downloads/{d.Id}/file?exp={linkExpires.ToUnixTimeSeconds()}&sig={signer.Sign(d.Id, linkExpires)}";
            fileName = $"{FileNames.Slug(title)}{(d.HasWatermark ? "-preview" : string.Empty)}.{extension}";
        }

        return new DownloadDto(d.Id, d.InvitationId, title, d.Format, d.Dpi, d.HasWatermark, status, d.FileSizeBytes,
            d.Status == DownloadStatus.Failed ? d.Error : null, fileName, url, d.CreatedAt, d.CompletedAt, d.ExpiresAt);
    }

    private async Task<int> LinkMinutesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var json = await content.GetSettingJsonAsync("downloads", cancellationToken);
            if (json is not null && JsonDocument.Parse(json).RootElement.TryGetProperty("signedUrlMinutes", out var value) &&
                value.TryGetInt32(out var minutes) && minutes is > 0 and <= 120)
                return minutes;
        }
        catch (JsonException)
        {
            // Malformed setting: use the default.
        }

        return DefaultLinkMinutes;
    }
}

internal static class FileNames
{
    /// <summary>"Ananya &amp; Rohan's Wedding" → "ananya-rohan-s-wedding" (ASCII-only, safe in headers and on every file system).</summary>
    public static string Slug(string title)
    {
        var builder = new StringBuilder();
        foreach (var c in title.Normalize(NormalizationForm.FormD))
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
                builder.Append(c);
            else if (c is >= 'A' and <= 'Z')
                builder.Append(char.ToLowerInvariant(c));
            else if (builder.Length > 0 && builder[^1] != '-' && char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                builder.Append('-');
        }

        var slug = builder.ToString().Trim('-');
        if (slug.Length > 60)
            slug = slug[..60].TrimEnd('-');
        return slug.Length == 0 ? "invitation" : slug;
    }
}
