using InviteCraft.Application.Common.Models;
using InviteCraft.Application.Features.Invitations;
using InviteCraft.Domain.Entities.Invitations;
using InviteCraft.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace InviteCraft.Infrastructure.Persistence.Repositories;

internal sealed class InvitationRepository(AppDbContext context) : Repository<Invitation>(context), IInvitationRepository
{
    public Task<Invitation?> GetOwnedAsync(Guid userId, Guid id, bool includeValues, CancellationToken cancellationToken = default)
    {
        var query = includeValues ? Set.Include(i => i.FieldValues) : Set.AsQueryable();
        return query.FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId, cancellationToken);
    }

    public Task<int> CountOwnedAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Set.CountAsync(i => i.UserId == userId, cancellationToken);

    public Task<Invitation?> GetBySlugAsync(string slug, bool includeValues, CancellationToken cancellationToken = default)
    {
        var query = includeValues ? Set.Include(i => i.FieldValues) : Set.AsQueryable();
        return query.FirstOrDefaultAsync(i => i.ShareSlug == slug, cancellationToken);
    }

    public Task IncrementViewCountAsync(Guid id, CancellationToken cancellationToken = default) =>
        Set.Where(i => i.Id == id).ExecuteUpdateAsync(set => set.SetProperty(i => i.ViewCount, i => i.ViewCount + 1), cancellationToken);

    public Task<PagedResult<InvitationListRow>> SearchOwnedAsync(Guid userId, InvitationQuery query, CancellationToken cancellationToken = default)
    {
        var source = Set.AsNoTracking().Where(i => i.UserId == userId);

        if (query.Status is { } status)
            source = source.Where(i => i.Status == status);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = QueryExtensions.ContainsPattern(query.Search);
            source = source.Where(i => EF.Functions.ILike(i.Title, pattern, QueryExtensions.LikeEscape));
        }

        var ordered = query.SortBy?.ToLowerInvariant() switch
        {
            "title" => source.OrderBy(i => i.Title, query.SortDir),
            "createdat" => source.OrderBy(i => i.CreatedAt, query.SortDir),
            _ => source.OrderByDescending(i => i.UpdatedAt),
        };

        return ordered
            .Select(i => new InvitationListRow(
                i.Id, i.Title, i.Status, i.TemplateId, i.Template.Name, i.Template.Slug,
                i.Template.Assets.Where(a => a.AssetType == TemplateAssetType.Thumbnail).Select(a => (Guid?)a.Id).FirstOrDefault(),
                i.UnlockedPackageId != null, i.UpdatedAt))
            .ToPagedResultAsync(query, cancellationToken);
    }

    public void Touch(Invitation invitation) =>
        Context.Entry(invitation).Property(i => i.UpdatedAt).IsModified = true;
}
