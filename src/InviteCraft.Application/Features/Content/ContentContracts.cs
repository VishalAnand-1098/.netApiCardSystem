using InviteCraft.Application.Common.Models;
using InviteCraft.Application.Features.Templates;

namespace InviteCraft.Application.Features.Content;

/// <summary>Festival collection. In the list endpoint <see cref="Templates"/> is a preview; in the detail endpoint it is complete.</summary>
public sealed record CollectionDto(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    string? BannerImageUrl,
    string? ThemeColor,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    int TemplateCount,
    IReadOnlyList<TemplateCardDto> Templates);

/// <summary>Customer quote for the homepage. The author is shown as "Priya S." — never the full name.</summary>
public sealed record TestimonialDto(Guid Id, string AuthorName, short Rating, string? Title, string? Comment, DateTimeOffset CreatedAt);

/// <summary>Public contact details (admin-editable in site_settings under "contact").</summary>
public sealed record SiteContactDto(string? Email, string? Phone, string? WhatsApp);

public sealed record CollectionRow(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    string? BannerImageUrl,
    string? ThemeColor,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    int TemplateCount);

public sealed record CollectionWithTemplates(CollectionRow Collection, IReadOnlyList<TemplateCardRow> Templates);

public sealed record TestimonialRow(Guid Id, string FullName, short Rating, string? Title, string? Comment, DateTimeOffset CreatedAt);

public interface IContentRepository
{
    /// <summary>Active collections inside their date window, each with its first <paramref name="previewCount"/> published templates.</summary>
    Task<IReadOnlyList<CollectionWithTemplates>> GetActiveCollectionsAsync(DateTimeOffset now, int previewCount, CancellationToken cancellationToken = default);

    Task<CollectionWithTemplates?> GetActiveCollectionAsync(string slug, DateTimeOffset now, int maxTemplates, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TestimonialRow>> GetFeaturedTestimonialsAsync(int limit, CancellationToken cancellationToken = default);

    Task<string?> GetSettingJsonAsync(string key, CancellationToken cancellationToken = default);
}

public static class ContentErrors
{
    public static readonly Error CollectionNotFound = Error.NotFound("Collection.NotFound", "Collection not found.");
}
