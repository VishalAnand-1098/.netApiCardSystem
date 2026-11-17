using InviteCraft.Application.Features.Content;
using InviteCraft.Application.Features.Templates;
using InviteCraft.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace InviteCraft.Infrastructure.Persistence.Repositories;

internal sealed class ContentRepository(AppDbContext context) : IContentRepository
{
    public async Task<IReadOnlyList<CollectionWithTemplates>> GetActiveCollectionsAsync(
        DateTimeOffset now, int previewCount, CancellationToken cancellationToken = default)
    {
        var collections = await ActiveCollections(now)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new CollectionRow(c.Id, c.Name, c.Slug, c.Description, c.BannerImageUrl, c.ThemeColor, c.StartsAt, c.EndsAt,
                c.Templates.Count(l => l.Template.Status == TemplateStatus.Published && l.Template.Category.IsActive)))
            .ToListAsync(cancellationToken);

        return await AttachTemplatesAsync(collections, previewCount, cancellationToken);
    }

    public async Task<CollectionWithTemplates?> GetActiveCollectionAsync(
        string slug, DateTimeOffset now, int maxTemplates, CancellationToken cancellationToken = default)
    {
        var collection = await ActiveCollections(now)
            .Where(c => c.Slug == slug)
            .Select(c => new CollectionRow(c.Id, c.Name, c.Slug, c.Description, c.BannerImageUrl, c.ThemeColor, c.StartsAt, c.EndsAt,
                c.Templates.Count(l => l.Template.Status == TemplateStatus.Published && l.Template.Category.IsActive)))
            .FirstOrDefaultAsync(cancellationToken);

        return collection is null ? null : (await AttachTemplatesAsync([collection], maxTemplates, cancellationToken))[0];
    }

    public async Task<IReadOnlyList<TestimonialRow>> GetFeaturedTestimonialsAsync(int limit, CancellationToken cancellationToken = default) =>
        await context.Reviews.AsNoTracking()
            .Where(r => r.TemplateId == null && r.Status == ReviewStatus.Approved && r.IsFeatured && r.Comment != null)
            .OrderByDescending(r => r.Rating).ThenByDescending(r => r.CreatedAt)
            .Take(limit)
            .Select(r => new TestimonialRow(r.Id, r.User.FullName, r.Rating, r.Title, r.Comment, r.CreatedAt))
            .ToListAsync(cancellationToken);

    public Task<string?> GetSettingJsonAsync(string key, CancellationToken cancellationToken = default) =>
        context.SiteSettings.AsNoTracking().Where(s => s.Key == key).Select(s => (string?)s.Value).FirstOrDefaultAsync(cancellationToken);

    private IQueryable<Domain.Entities.Catalog.FestivalCollection> ActiveCollections(DateTimeOffset now) =>
        context.FestivalCollections.AsNoTracking()
            .Where(c => c.IsActive && (c.StartsAt == null || c.StartsAt <= now) && (c.EndsAt == null || c.EndsAt >= now));

    /// <summary>Loads each collection's visible (published, active category) templates in curated order, up to <paramref name="perCollection"/>.</summary>
    private async Task<IReadOnlyList<CollectionWithTemplates>> AttachTemplatesAsync(
        List<CollectionRow> collections, int perCollection, CancellationToken cancellationToken)
    {
        if (collections.Count == 0)
            return [];

        var collectionIds = collections.Select(c => c.Id).ToList();
        var links = await context.FestivalCollectionTemplates.AsNoTracking()
            .Where(l => collectionIds.Contains(l.CollectionId)
                        && l.Template.Status == TemplateStatus.Published
                        && l.Template.Category.IsActive
                        && (l.Template.SubCategory == null || l.Template.SubCategory.IsActive))
            .Select(l => new { l.CollectionId, l.TemplateId, l.SortOrder })
            .ToListAsync(cancellationToken);

        var idsByCollection = links
            .GroupBy(l => l.CollectionId)
            .ToDictionary(g => g.Key, g => g.OrderBy(l => l.SortOrder).Take(perCollection).Select(l => l.TemplateId).ToList());

        var templateIds = idsByCollection.Values.SelectMany(ids => ids).Distinct().ToList();
        var cards = await context.Templates.AsNoTracking()
            .Where(t => templateIds.Contains(t.Id))
            .Select(TemplateRepository.CardProjection)
            .ToDictionaryAsync(r => r.Id, cancellationToken);

        return collections
            .Select(c => new CollectionWithTemplates(c,
                idsByCollection.GetValueOrDefault(c.Id, []).Where(cards.ContainsKey).Select(id => cards[id]).ToList()))
            .ToList();
    }
}
