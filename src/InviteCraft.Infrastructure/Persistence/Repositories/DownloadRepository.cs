using InviteCraft.Application.Common.Models;
using InviteCraft.Application.Features.Rendering;
using InviteCraft.Domain.Entities.Invitations;
using InviteCraft.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace InviteCraft.Infrastructure.Persistence.Repositories;

internal sealed class DownloadRepository(AppDbContext context) : Repository<Download>(context), IDownloadRepository
{
    public Task<Download?> GetOwnedAsync(Guid userId, Guid id, CancellationToken cancellationToken = default) =>
        Set.Include(d => d.Invitation).FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId, cancellationToken);

    public Task<Download?> GetForRenderAsync(Guid id, CancellationToken cancellationToken = default) =>
        Set.Include(d => d.Invitation).FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    public async Task<PagedResult<Download>> SearchOwnedAsync(Guid userId, DownloadQuery query, CancellationToken cancellationToken = default)
    {
        var source = Set.AsNoTracking().Include(d => d.Invitation).Where(d => d.UserId == userId);
        if (query.InvitationId is { } invitationId)
            source = source.Where(d => d.InvitationId == invitationId);

        var total = await source.CountAsync(cancellationToken);
        var items = await source
            .OrderByDescending(d => d.CreatedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<Download>(items, query.Page, query.PageSize, total);
    }

    public Task<int> CountBillableAsync(Guid invitationId, CancellationToken cancellationToken = default) =>
        Set.CountAsync(d => d.InvitationId == invitationId && !d.HasWatermark && d.Status != DownloadStatus.Failed, cancellationToken);

    public Task<int> CountInProgressAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Set.CountAsync(d => d.UserId == userId && (d.Status == DownloadStatus.Queued || d.Status == DownloadStatus.Rendering), cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetUnfinishedIdsAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default) =>
        await Set.AsNoTracking()
            .Where(d => (d.Status == DownloadStatus.Queued || d.Status == DownloadStatus.Rendering) && d.CreatedAt <= olderThan)
            .OrderBy(d => d.CreatedAt)
            .Select(d => d.Id)
            .ToListAsync(cancellationToken);
}
