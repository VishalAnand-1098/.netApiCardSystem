using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Application.Common.Models;
using InviteCraft.Domain.Entities.Templates;
using InviteCraft.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace InviteCraft.Application.Features.Templates;

public interface ITemplateAssetService
{
    Task<Result<TemplateAssetDto>> UploadAsync(Guid templateId, TemplateAssetType assetType, int pageIndex, FileUpload file, CancellationToken cancellationToken = default);
    Task<Result> DeleteAsync(Guid templateId, Guid assetId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Upload pipeline: size check → content sniffing (never trust name/MIME) → dimension limits →
/// private original + public WebP web/thumb variants → database row. Only one thumbnail per template.
/// </summary>
internal sealed class TemplateAssetService(
    ITemplateRepository templates,
    IUnitOfWork unitOfWork,
    IFileStorage storage,
    IImageProcessor images,
    TemplateMapper mapper,
    IAuditLogger audit,
    ILogger<TemplateAssetService> logger) : ITemplateAssetService
{
    public const long MaxImageBytes = 15 * 1024 * 1024;
    public const int MinDimension = 200;
    public const int MaxDimension = 6000;
    public const int ThumbSize = 480;
    public const int MaxPreviewAssets = 8;

    public async Task<Result<TemplateAssetDto>> UploadAsync(
        Guid templateId, TemplateAssetType assetType, int pageIndex, FileUpload file, CancellationToken cancellationToken = default)
    {
        if (pageIndex is < 0 or > 3)
            return TemplateErrors.PageOutOfRange;
        if (file.Length is <= 0 or > MaxImageBytes)
            return TemplateErrors.ImageTooLarge(MaxImageBytes);

        var template = await templates.GetAggregateAsync(templateId, track: true, cancellationToken);
        if (template is null)
            return TemplateErrors.NotFound;

        // Buffer once: the bytes are read several times (sniff, original, two variants). Bounded by MaxImageBytes.
        using var buffer = new MemoryStream();
        await file.Content.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length > MaxImageBytes)
            return TemplateErrors.ImageTooLarge(MaxImageBytes);

        buffer.Position = 0;
        var info = images.Inspect(buffer);
        if (info is null)
            return TemplateErrors.UnsupportedImage;
        if (Math.Min(info.Width, info.Height) < MinDimension || Math.Max(info.Width, info.Height) > MaxDimension)
            return TemplateErrors.ImageDimensions(MinDimension, MaxDimension);

        if (assetType == TemplateAssetType.Preview &&
            template.Assets.Count(a => a.AssetType == TemplateAssetType.Preview) >= MaxPreviewAssets)
            return Error.Validation("TemplateAsset.TooMany", $"A template can have at most {MaxPreviewAssets} preview images.", "file");

        var assetId = Guid.CreateVersion7();
        var originalKey = TemplateStorageKeys.Original(templateId, assetId, info.Extension);
        var webMax = assetType switch
        {
            TemplateAssetType.Background or TemplateAssetType.Decoration => 2000,
            TemplateAssetType.Thumbnail => 900,
            _ => 1600,
        };

        try
        {
            buffer.Position = 0;
            await storage.SaveAsync(originalKey, buffer, info.MimeType, cancellationToken);

            buffer.Position = 0;
            using (var web = new MemoryStream(images.CreateWebVariant(buffer, webMax, quality: 85)))
                await storage.SaveAsync(TemplateStorageKeys.Web(templateId, assetId), web, "image/webp", cancellationToken);

            buffer.Position = 0;
            using (var thumb = new MemoryStream(images.CreateWebVariant(buffer, ThumbSize, quality: 78)))
                await storage.SaveAsync(TemplateStorageKeys.Thumb(templateId, assetId), thumb, "image/webp", cancellationToken);
        }
        catch
        {
            await DeleteFilesAsync(templateId, assetId);
            throw;
        }

        // A template has exactly one thumbnail: a new upload replaces the previous one.
        var replaced = assetType == TemplateAssetType.Thumbnail
            ? template.Assets.Where(a => a.AssetType == TemplateAssetType.Thumbnail).ToList()
            : [];
        foreach (var old in replaced)
            template.Assets.Remove(old);

        var asset = new TemplateAsset
        {
            Id = assetId,
            TemplateId = templateId,
            AssetType = assetType,
            Variant = AssetVariant.Original,
            StorageKey = originalKey,
            PublicUrl = mapper.WebUrl(templateId, assetId),
            MimeType = info.MimeType,
            SizeBytes = buffer.Length,
            WidthPx = info.Width,
            HeightPx = info.Height,
            PageIndex = pageIndex,
            SortOrder = template.Assets.Count(a => a.AssetType == assetType),
        };
        template.Assets.Add(asset);
        audit.Log("TemplateAssetUploaded", nameof(Template), templateId.ToString(),
            details: new { assetId, assetType, info.Width, info.Height, bytes = buffer.Length, file = Path.GetFileName(file.FileName) });

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await DeleteFilesAsync(templateId, assetId); // no orphaned files when the row could not be saved
            throw;
        }

        foreach (var old in replaced)
            await DeleteFilesAsync(templateId, old.Id);

        return new TemplateAssetDto(asset.Id, asset.AssetType, asset.PageIndex, asset.SortOrder,
            mapper.WebUrl(templateId, assetId), mapper.ThumbUrl(templateId, assetId),
            asset.WidthPx, asset.HeightPx, asset.SizeBytes, asset.MimeType);
    }

    public async Task<Result> DeleteAsync(Guid templateId, Guid assetId, CancellationToken cancellationToken = default)
    {
        var template = await templates.GetAggregateAsync(templateId, track: true, cancellationToken);
        if (template is null)
            return TemplateErrors.NotFound;

        var asset = template.Assets.FirstOrDefault(a => a.Id == assetId);
        if (asset is null)
            return TemplateErrors.AssetNotFound;

        var design = TemplateJson.Deserialize<TemplateDesign>(template.Design) ?? TemplateDesign.Empty();
        if (design.ReferencedAssetIds().Contains(assetId))
            return TemplateErrors.AssetInUse;

        template.Assets.Remove(asset);
        audit.Log("TemplateAssetDeleted", nameof(Template), templateId.ToString(), details: new { assetId, asset.AssetType });
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await DeleteFilesAsync(templateId, assetId);
        return Result.Success();
    }

    private async Task DeleteFilesAsync(Guid templateId, Guid assetId)
    {
        try
        {
            await storage.DeletePrefixAsync(TemplateStorageKeys.PublicFolder(templateId, assetId), CancellationToken.None);
            await storage.DeletePrefixAsync(TemplateStorageKeys.PrivateFolder(templateId, assetId), CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not delete stored files of asset {AssetId}", assetId);
        }
    }
}
