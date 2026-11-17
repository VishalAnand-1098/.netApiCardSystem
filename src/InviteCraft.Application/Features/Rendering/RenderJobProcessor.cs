using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Application.Features.Content;
using InviteCraft.Application.Features.Invitations;
using InviteCraft.Application.Features.Templates;
using InviteCraft.Domain.Entities.Invitations;
using InviteCraft.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace InviteCraft.Application.Features.Rendering;

/// <summary>Runs one queued download: draw the card, store the file, update the row. Called by the background worker.</summary>
public interface IRenderJobProcessor
{
    Task ProcessAsync(Guid downloadId, CancellationToken cancellationToken = default);
}

internal sealed class RenderJobProcessor(
    IDownloadRepository downloads,
    IInvitationRepository invitations,
    ITemplateRepository templates,
    IContentRepository content,
    ICardRenderer renderer,
    IFileStorage storage,
    IUnitOfWork unitOfWork,
    TemplateMapper mapper,
    TimeProvider timeProvider,
    ILogger<RenderJobProcessor> logger) : IRenderJobProcessor
{
    public static string StorageKey(Guid userId, Guid downloadId, string extension) =>
        $"{IFileStorage.PrivatePrefix}downloads/{userId}/{downloadId}.{extension}";

    public async Task ProcessAsync(Guid downloadId, CancellationToken cancellationToken = default)
    {
        var download = await downloads.GetForRenderAsync(downloadId, cancellationToken);
        if (download is null || download.Status is not (DownloadStatus.Queued or DownloadStatus.Rendering))
            return; // already finished, expired or removed

        download.Status = DownloadStatus.Rendering;
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var started = timeProvider.GetTimestamp();
        try
        {
            var invitation = await invitations.GetOwnedAsync(download.UserId, download.InvitationId, includeValues: true, cancellationToken)
                ?? throw new InvalidOperationException("The invitation no longer exists.");
            var template = await templates.GetAggregateAsync(invitation.TemplateId, track: false, cancellationToken)
                ?? throw new InvalidOperationException("The template no longer exists.");
            var packages = await templates.GetActivePackagesAsync(cancellationToken);

            var request = new CardRenderRequest(
                mapper.ToDetail(template, packages, includeInactive: false),
                invitation.Language,
                PageIndex: 0,
                RenderValueMapper.FromInvitation(invitation, template, storage),
                download.Format,
                download.Dpi,
                download.HasWatermark ? await Watermarks.ReadAsync(content, cancellationToken) : null);

            var file = await renderer.RenderAsync(request, cancellationToken);

            var key = StorageKey(download.UserId, download.Id, file.Extension);
            using (var stream = new MemoryStream(file.Content, writable: false))
                await storage.SaveAsync(key, stream, file.ContentType, cancellationToken);

            download.StorageKey = key;
            download.FileSizeBytes = file.Content.Length;
            download.Status = DownloadStatus.Ready;
            download.Error = null;
            download.CompletedAt = timeProvider.GetUtcNow();
            await unitOfWork.SaveChangesAsync(CancellationToken.None);

            logger.LogInformation("Download {DownloadId} ready: {Format} {Width}x{Height}px, {Bytes} bytes in {Elapsed:N0} ms",
                downloadId, download.Format, file.WidthPx, file.HeightPx, file.Content.Length, timeProvider.GetElapsedTime(started).TotalMilliseconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // shutting down: the row stays Rendering and is re-queued on the next start
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Download {DownloadId} failed", downloadId);
            download.Status = DownloadStatus.Failed;
            download.Error = DownloadErrors.RenderUnavailable.Message;
            download.CompletedAt = timeProvider.GetUtcNow();
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
        }
    }
}
