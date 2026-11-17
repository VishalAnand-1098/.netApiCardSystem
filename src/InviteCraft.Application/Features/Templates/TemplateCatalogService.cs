using InviteCraft.Application.Common.Models;
using InviteCraft.Domain.Entities.Templates;

namespace InviteCraft.Application.Features.Templates;

/// <summary>Storefront template browsing. Only published templates in active categories are visible.</summary>
public interface ITemplateCatalogService
{
    Task<PagedResult<TemplateCardDto>> SearchAsync(TemplateSearchQuery query, CancellationToken cancellationToken = default);
    Task<Result<TemplateDetailDto>> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PackageDto>> GetPackagesAsync(CancellationToken cancellationToken = default);
}

public sealed record PackageDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    IReadOnlyList<string> Features,
    decimal BasePrice,
    IReadOnlyList<string> AllowedFormats,
    int MaxDpi,
    int? MaxDownloads,
    bool RemovesWatermark,
    int? EditWindowDays,
    bool IncludesCustomDesign,
    bool IsRecommended,
    int SortOrder);

internal sealed class TemplateCatalogService(ITemplateRepository templates, TemplateMapper mapper) : ITemplateCatalogService
{
    public async Task<PagedResult<TemplateCardDto>> SearchAsync(TemplateSearchQuery query, CancellationToken cancellationToken = default)
    {
        var page = await templates.SearchPublishedAsync(query, cancellationToken);
        return new PagedResult<TemplateCardDto>(page.Items.Select(mapper.ToCard).ToList(), page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<Result<TemplateDetailDto>> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var template = await templates.GetPublishedAggregateBySlugAsync(slug.Trim().ToLowerInvariant(), cancellationToken);
        if (template is null)
            return TemplateErrors.NotFound;

        await templates.IncrementViewCountAsync(template.Id, cancellationToken);
        var packages = await templates.GetActivePackagesAsync(cancellationToken);
        return mapper.ToDetail(template, packages, includeInactive: false);
    }

    public async Task<IReadOnlyList<PackageDto>> GetPackagesAsync(CancellationToken cancellationToken = default) =>
        (await templates.GetActivePackagesAsync(cancellationToken))
            .OrderBy(p => p.SortOrder)
            .Select(p => new PackageDto(p.Id, p.Code, p.Name, p.Description,
                TemplateJson.Deserialize<List<string>>(p.Features) ?? [], p.BasePrice, p.AllowedFormats, p.MaxDpi,
                p.MaxDownloads, p.RemovesWatermark, p.EditWindowDays, p.IncludesCustomDesign, p.IsRecommended, p.SortOrder))
            .ToList();
}
