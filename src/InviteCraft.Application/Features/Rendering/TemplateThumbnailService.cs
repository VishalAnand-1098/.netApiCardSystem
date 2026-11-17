using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Application.Common.Models;
using InviteCraft.Application.Features.Templates;
using InviteCraft.Domain.Enums;

namespace InviteCraft.Application.Features.Rendering;

public interface ITemplateThumbnailService
{
    /// <summary>
    /// Draws the template with its sample content (defaults / placeholders) through the same renderer customers' downloads use,
    /// and stores the result as the template's thumbnail — so marketplace cards always match the real output.
    /// </summary>
    Task<Result<TemplateAssetDto>> GenerateAsync(Guid templateId, CancellationToken cancellationToken = default);
}

internal sealed class TemplateThumbnailService(
    IAdminTemplateService templates,
    ITemplateAssetService assets,
    ICardRenderer renderer) : ITemplateThumbnailService
{
    /// <summary>Longest side of the rendered thumbnail in pixels (stored as a 900 px WebP, with a 480 px list variant).</summary>
    private const int LongestSidePx = 1000;

    public async Task<Result<TemplateAssetDto>> GenerateAsync(Guid templateId, CancellationToken cancellationToken = default)
    {
        var loaded = await templates.GetAsync(templateId, cancellationToken);
        if (!loaded.IsSuccess)
            return Result.Failure<TemplateAssetDto>(loaded.Errors);

        var template = loaded.Value.Template;
        var longestMm = Math.Max(template.WidthMm, template.HeightMm);
        var dpi = Math.Clamp((int)Math.Round(LongestSidePx / (longestMm / 25.4m)), 72, 300);

        var request = new CardRenderRequest(
            template,
            template.SupportedLanguages.FirstOrDefault() ?? "en",
            PageIndex: 0,
            RenderValueMapper.Sample(template),
            DownloadFormat.Jpg,
            dpi,
            Watermark: null);

        var file = await renderer.RenderAsync(request, cancellationToken);

        using var stream = new MemoryStream(file.Content, writable: false);
        return await assets.UploadAsync(templateId, TemplateAssetType.Thumbnail, 0,
            new FileUpload(stream, "thumbnail.jpg", file.ContentType, file.Content.Length), cancellationToken);
    }
}
