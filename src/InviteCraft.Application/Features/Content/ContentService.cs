using InviteCraft.Application.Common.Models;
using InviteCraft.Application.Features.Templates;

namespace InviteCraft.Application.Features.Content;

public interface IContentService
{
    Task<IReadOnlyList<CollectionDto>> GetCollectionsAsync(CancellationToken cancellationToken = default);
    Task<Result<CollectionDto>> GetCollectionAsync(string slug, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TestimonialDto>> GetTestimonialsAsync(int limit, CancellationToken cancellationToken = default);
    Task<SiteContactDto> GetContactAsync(CancellationToken cancellationToken = default);
}

/// <summary>Public, read-only content for the storefront (collections, testimonials, contact details).</summary>
internal sealed class ContentService(IContentRepository content, TemplateMapper mapper, TimeProvider timeProvider) : IContentService
{
    private const int CollectionPreviewCount = 4;
    private const int CollectionMaxTemplates = 60;
    private const int MaxTestimonials = 12;

    public async Task<IReadOnlyList<CollectionDto>> GetCollectionsAsync(CancellationToken cancellationToken = default)
    {
        var rows = await content.GetActiveCollectionsAsync(timeProvider.GetUtcNow(), CollectionPreviewCount, cancellationToken);
        return rows.Where(r => r.Collection.TemplateCount > 0).Select(ToDto).ToList();
    }

    public async Task<Result<CollectionDto>> GetCollectionAsync(string slug, CancellationToken cancellationToken = default)
    {
        var row = await content.GetActiveCollectionAsync(slug.Trim().ToLowerInvariant(), timeProvider.GetUtcNow(), CollectionMaxTemplates, cancellationToken);
        return row is null ? ContentErrors.CollectionNotFound : ToDto(row);
    }

    public async Task<IReadOnlyList<TestimonialDto>> GetTestimonialsAsync(int limit, CancellationToken cancellationToken = default)
    {
        var rows = await content.GetFeaturedTestimonialsAsync(Math.Clamp(limit, 1, MaxTestimonials), cancellationToken);
        return rows.Select(r => new TestimonialDto(r.Id, DisplayName(r.FullName), r.Rating, r.Title, r.Comment, r.CreatedAt)).ToList();
    }

    public async Task<SiteContactDto> GetContactAsync(CancellationToken cancellationToken = default)
    {
        var json = await content.GetSettingJsonAsync("contact", cancellationToken);
        try
        {
            return TemplateJson.Deserialize<SiteContactDto>(json) ?? new SiteContactDto(null, null, null);
        }
        catch (System.Text.Json.JsonException)
        {
            return new SiteContactDto(null, null, null); // a malformed admin edit must not break the Contact page
        }
    }

    /// <summary>"Priya Sharma" → "Priya S."; protects customers' full names on a public page.</summary>
    internal static string DisplayName(string fullName)
    {
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            0 => "Customer",
            1 => parts[0],
            _ => $"{parts[0]} {char.ToUpperInvariant(parts[^1][0])}.",
        };
    }

    private CollectionDto ToDto(CollectionWithTemplates row)
    {
        var c = row.Collection;
        return new CollectionDto(c.Id, c.Name, c.Slug, c.Description, c.BannerImageUrl, c.ThemeColor, c.StartsAt, c.EndsAt,
            c.TemplateCount, row.Templates.Select(mapper.ToCard).ToList());
    }
}
