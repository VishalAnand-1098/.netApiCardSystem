using System.Net;
using System.Security.Cryptography;
using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Application.Common.Models;
using InviteCraft.Application.Features.Content;
using InviteCraft.Application.Features.Payments;
using InviteCraft.Application.Features.Invitations;
using InviteCraft.Application.Features.Rendering;
using InviteCraft.Application.Features.Templates;
using InviteCraft.Domain.Entities.Invitations;
using InviteCraft.Domain.Entities.Templates;
using InviteCraft.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InviteCraft.Application.Features.Sharing;

internal sealed class SharingService(
    IInvitationRepository invitations,
    ITemplateRepository templates,
    IContentRepository content,
    ICardRenderer renderer,
    IFileStorage storage,
    IUnitOfWork unitOfWork,
    IPersistenceConflicts conflicts,
    TemplateMapper mapper,
    IAuditLogger audit,
    TimeProvider timeProvider,
    IOptions<SharingSettings> options,
    ILogger<SharingService> logger) : ISharingService
{
    /// <summary>Unambiguous lowercase letters and digits (no 0/o, 1/l) — the code is read aloud and typed from phones.</summary>
    private const string SlugAlphabet = "abcdefghjkmnpqrstuvwxyz23456789";
    public const int SlugLength = 10;

    public static string ImageKey(string slug) => $"{IFileStorage.PublicPrefix}share/{slug}.jpg";

    // ------------------------------------------------------------------ owner

    public async Task<Result<InvitationSharingDto>> GetAsync(Guid userId, Guid invitationId, string origin, CancellationToken cancellationToken = default)
    {
        var invitation = await invitations.GetOwnedAsync(userId, invitationId, includeValues: false, cancellationToken);
        if (invitation is null)
            return InvitationErrors.NotFound;

        var template = await templates.GetAggregateAsync(invitation.TemplateId, track: false, cancellationToken);
        return ToDto(invitation, template, origin);
    }

    public async Task<Result<InvitationSharingDto>> UpdateAsync(
        Guid userId, Guid invitationId, UpdateSharingRequest request, string origin, CancellationToken cancellationToken = default)
    {
        if (request.Visibility == InvitationVisibility.Public)
            return SharingErrors.PublicNotSupported;

        var invitation = await invitations.GetOwnedAsync(userId, invitationId, includeValues: true, cancellationToken);
        if (invitation is null)
            return InvitationErrors.NotFound;

        var template = await templates.GetAggregateAsync(invitation.TemplateId, track: false, cancellationToken);
        if (template is null)
            return TemplateErrors.NotFound;

        if (request.Visibility == InvitationVisibility.LinkOnly)
        {
            invitation.ShareSlug ??= NewSlug();
            invitation.Visibility = InvitationVisibility.LinkOnly;
            invitation.ShareExpiresAt = null;
            await RenderImageQuietlyAsync(invitation, template, cancellationToken); // fresh preview every time sharing is (re)enabled
            audit.Log("InvitationShared", nameof(Invitation), invitation.Id.ToString(), userId, new { invitation.ShareSlug });
        }
        else
        {
            invitation.Visibility = InvitationVisibility.Private;
            await DeleteImageQuietlyAsync(invitation); // nothing of a private invitation stays publicly reachable
            audit.Log("InvitationUnshared", nameof(Invitation), invitation.Id.ToString(), userId, new { invitation.ShareSlug });
        }

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (conflicts.IsConflict(ex))
        {
            conflicts.DiscardChanges(); // the one-in-a-billion slug collision or a concurrent edit: one retry with a fresh read
            return await UpdateRetryAsync(userId, invitationId, request, origin, cancellationToken);
        }

        return ToDto(invitation, template, origin);
    }

    private async Task<Result<InvitationSharingDto>> UpdateRetryAsync(
        Guid userId, Guid invitationId, UpdateSharingRequest request, string origin, CancellationToken cancellationToken)
    {
        var invitation = await invitations.GetOwnedAsync(userId, invitationId, includeValues: true, cancellationToken);
        if (invitation is null)
            return InvitationErrors.NotFound;
        var template = await templates.GetAggregateAsync(invitation.TemplateId, track: false, cancellationToken);
        if (template is null)
            return TemplateErrors.NotFound;

        invitation.Visibility = request.Visibility;
        if (request.Visibility == InvitationVisibility.LinkOnly)
        {
            invitation.ShareSlug = NewSlug();
            await RenderImageQuietlyAsync(invitation, template, cancellationToken);
        }
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToDto(invitation, template, origin);
    }

    // ------------------------------------------------------------------ public

    public async Task<Result<PublicInvitationDto>> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var invitation = await FindSharedAsync(slug, includeValues: true, cancellationToken);
        if (invitation is null)
            return SharingErrors.NotFound;

        var template = await templates.GetAggregateAsync(invitation.TemplateId, track: false, cancellationToken);
        if (template is null)
            return SharingErrors.NotFound;

        // The preview image went stale while shared (the card was edited): refresh it for the next OG fetch.
        if (invitation.PreviewImageKey is null || invitation.LastRenderedAt < invitation.UpdatedAt)
        {
            if (await RenderImageQuietlyAsync(invitation, template, cancellationToken))
                await SaveQuietlyAsync(cancellationToken);
        }

        await invitations.IncrementViewCountAsync(invitation.Id, cancellationToken);

        var fields = template.EditableFields.Where(f => f.IsActive).ToDictionary(f => f.Id);
        var values = invitation.FieldValues
            .Where(v => fields.ContainsKey(v.FieldId))
            .Select(v =>
            {
                var field = fields[v.FieldId];
                var imageUrl = field.FieldType == EditableFieldType.Image && Guid.TryParse(v.Value, out var assetId)
                    ? storage.GetPublicUrl(InvitationStorageKeys.Web(invitation.Id, assetId))
                    : null;
                return new InvitationValueDto(field.FieldKey, v.Value, imageUrl,
                    TemplateJson.Deserialize<FieldStyleOverride>(v.StyleOverride),
                    TemplateJson.Deserialize<FieldPosition>(v.FrameOverride));
            })
            .OrderBy(v => v.FieldKey, StringComparer.Ordinal)
            .ToList();

        return new PublicInvitationDto(
            slug, invitation.Title, invitation.Language, Watermarked(invitation, template),
            ImageUrl(invitation, template), values, mapper.ToDetail(template, [], includeInactive: false));
    }

    public async Task<SharePreview?> GetPreviewAsync(string slug, CancellationToken cancellationToken = default)
    {
        var invitation = await FindSharedAsync(slug, includeValues: false, cancellationToken);
        if (invitation is null)
            return null;

        var template = await templates.GetAggregateAsync(invitation.TemplateId, track: false, cancellationToken);
        return new SharePreview(slug, invitation.Title, template?.Name ?? "Invitation", template is null ? null : ImageUrl(invitation, template));
    }

    // ------------------------------------------------------------------ helpers

    private async Task<Invitation?> FindSharedAsync(string slug, bool includeValues, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(slug) || slug.Length > 32)
            return null;

        var invitation = await invitations.GetBySlugAsync(slug.Trim().ToLowerInvariant(), includeValues, cancellationToken);
        if (invitation is null || invitation.Visibility == InvitationVisibility.Private)
            return null;
        if (invitation.ShareExpiresAt is { } expires && expires < timeProvider.GetUtcNow())
            return null;
        return invitation;
    }

    private static bool Watermarked(Invitation invitation, Template template) =>
        template.Tier == TemplateTier.Premium && !invitation.IsUnlocked;

    private string? ImageUrl(Invitation invitation, Template template)
    {
        if (invitation.PreviewImageKey is not null)
            return storage.GetPublicUrl(invitation.PreviewImageKey);
        var thumbnail = template.Assets.FirstOrDefault(a => a.AssetType == TemplateAssetType.Thumbnail);
        return thumbnail is null ? null : mapper.ThumbUrl(template.Id, thumbnail.Id);
    }

    /// <summary>Renders the public preview JPG. Best effort: a render problem never blocks sharing (the template thumbnail stands in).</summary>
    private async Task<bool> RenderImageQuietlyAsync(Invitation invitation, Template template, CancellationToken cancellationToken)
    {
        if (!options.Value.RenderImages || invitation.ShareSlug is null)
            return false;

        try
        {
            var request = new CardRenderRequest(
                mapper.ToDetail(template, [], includeInactive: false),
                invitation.Language,
                PageIndex: 0,
                RenderValueMapper.FromInvitation(invitation, template, storage),
                DownloadFormat.Jpg,
                Dpi: 96,
                Watermarked(invitation, template) ? await Watermarks.ReadAsync(content, cancellationToken) : null);

            var file = await renderer.RenderAsync(request, cancellationToken);
            var key = ImageKey(invitation.ShareSlug);
            using (var stream = new MemoryStream(file.Content, writable: false))
                await storage.SaveAsync(key, stream, file.ContentType, cancellationToken);

            invitation.PreviewImageKey = key;
            invitation.LastRenderedAt = timeProvider.GetUtcNow();
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Share preview image for invitation {InvitationId} could not be rendered; using the template thumbnail", invitation.Id);
            return false;
        }
    }

    private async Task DeleteImageQuietlyAsync(Invitation invitation)
    {
        if (invitation.PreviewImageKey is null)
            return;
        try
        {
            await storage.DeleteAsync(invitation.PreviewImageKey, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Stored share image {Key} could not be deleted", invitation.PreviewImageKey);
        }
        invitation.PreviewImageKey = null;
        invitation.LastRenderedAt = null;
    }

    private async Task SaveQuietlyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (conflicts.IsConflict(ex))
        {
            conflicts.DiscardChanges(); // the owner saved at the same moment; the next view refreshes again
        }
    }

    private InvitationSharingDto ToDto(Invitation invitation, Template? template, string origin)
    {
        var shared = invitation.Visibility != InvitationVisibility.Private && invitation.ShareSlug is not null;
        var shareUrl = shared ? $"{origin.TrimEnd('/')}/s/{invitation.ShareSlug}" : null;
        var message = shareUrl is null ? null : $"You're invited! “{invitation.Title}” — see the invitation here: {shareUrl}";
        return new InvitationSharingDto(
            invitation.Visibility,
            shared ? invitation.ShareSlug : null,
            shareUrl,
            message is null ? null : $"https://wa.me/?text={WebUtility.UrlEncode(message)}",
            template is not null && Watermarked(invitation, template),
            invitation.ViewCount);
    }

    private static string NewSlug()
    {
        Span<byte> bytes = stackalloc byte[SlugLength];
        RandomNumberGenerator.Fill(bytes);
        Span<char> chars = stackalloc char[SlugLength];
        for (var i = 0; i < SlugLength; i++)
            chars[i] = SlugAlphabet[bytes[i] % SlugAlphabet.Length];
        return new string(chars);
    }
}
