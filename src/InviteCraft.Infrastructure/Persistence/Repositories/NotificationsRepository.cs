using InviteCraft.Application.Common.Models;
using InviteCraft.Application.Features.Notifications;
using InviteCraft.Domain.Entities.Platform;
using InviteCraft.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace InviteCraft.Infrastructure.Persistence.Repositories;

internal sealed class NotificationsRepository(AppDbContext context) : INotificationsRepository
{
    public void Add(Notification notification) => context.Notifications.Add(notification);

    public Task<PagedResult<NotificationDto>> ListInAppAsync(Guid userId, NotificationQuery query, CancellationToken cancellationToken = default)
    {
        var source = context.Notifications.AsNoTracking()
            .Where(n => n.UserId == userId && n.Channel == NotificationChannel.InApp);
        if (query.UnreadOnly)
            source = source.Where(n => n.ReadAt == null);

        return source.OrderByDescending(n => n.CreatedAt)
            .Select(n => new NotificationDto(n.Id, n.Type, n.Title, n.Body, n.Data, n.CreatedAt, n.ReadAt))
            .ToPagedResultAsync(query, cancellationToken);
    }

    public Task<int> CountUnreadAsync(Guid userId, CancellationToken cancellationToken = default) =>
        context.Notifications.CountAsync(n => n.UserId == userId && n.Channel == NotificationChannel.InApp && n.ReadAt == null, cancellationToken);

    public Task<int> MarkReadAsync(Guid userId, IReadOnlyList<Guid>? ids, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var target = context.Notifications
            .Where(n => n.UserId == userId && n.Channel == NotificationChannel.InApp && n.ReadAt == null);
        if (ids is not null)
            target = target.Where(n => ids.Contains(n.Id));
        return target.ExecuteUpdateAsync(set => set.SetProperty(n => n.ReadAt, now), cancellationToken);
    }

    public async Task<IReadOnlyList<Notification>> GetPendingEmailAsync(int take, int maxAttempts, CancellationToken cancellationToken = default) =>
        await context.Notifications
            .Where(n => n.Channel == NotificationChannel.Email && n.Status == NotificationStatus.Pending && n.Attempts < maxAttempts)
            .OrderBy(n => n.CreatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);
}
