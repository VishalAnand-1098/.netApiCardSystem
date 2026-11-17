using InviteCraft.Application.Common.Models;
using InviteCraft.Domain.Entities.Commerce;
using InviteCraft.Domain.Entities.Platform;

namespace InviteCraft.Application.Features.Notifications;

/// <summary>Bound from the "Notifications" section by the Infrastructure layer.</summary>
public sealed class NotificationSettings
{
    /// <summary>How often the email worker looks for pending notifications.</summary>
    public int SweepSeconds { get; set; } = 15;

    /// <summary>Send attempts before a notification is marked Failed.</summary>
    public int MaxAttempts { get; set; } = 3;

    public int BatchSize { get; set; } = 20;
}

public static class NotificationTypes
{
    public const string OrderPaid = "order.paid";
    public const string OrderRefunded = "order.refunded";
}

public sealed record NotificationDto(Guid Id, string Type, string Title, string Body, string? Data, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);

/// <summary>?unreadOnly=&amp;page=&amp;pageSize=</summary>
public sealed record NotificationQuery : PagedRequest
{
    public bool UnreadOnly { get; init; }
}

/// <param name="Ids">Null or empty = mark everything read.</param>
public sealed record MarkReadRequest(IReadOnlyList<Guid>? Ids);

public interface INotificationsRepository
{
    /// <summary>Queued into the caller's unit of work; the caller saves.</summary>
    void Add(Notification notification);

    /// <summary>The customer's in-app feed, newest first (email plumbing rows are not shown).</summary>
    Task<PagedResult<NotificationDto>> ListInAppAsync(Guid userId, NotificationQuery query, CancellationToken cancellationToken = default);

    Task<int> CountUnreadAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Atomic; only the caller's own unread rows. Returns how many were marked.</summary>
    Task<int> MarkReadAsync(Guid userId, IReadOnlyList<Guid>? ids, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>Tracked pending email notifications that still have attempts left, oldest first.</summary>
    Task<IReadOnlyList<Notification>> GetPendingEmailAsync(int take, int maxAttempts, CancellationToken cancellationToken = default);
}

public interface INotificationService
{
    // ---- customer feed
    Task<PagedResult<NotificationDto>> ListAsync(Guid userId, NotificationQuery query, CancellationToken cancellationToken = default);
    Task<int> CountUnreadAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<int> MarkReadAsync(Guid userId, MarkReadRequest request, CancellationToken cancellationToken = default);

    // ---- enqueue (adds rows to the caller's unit of work; the caller saves, so notifications land exactly with the state change)
    void OrderPaid(Order order);
    void OrderRefunded(Order order);
}

/// <summary>Sends the pending email notifications. Called by the background worker.</summary>
public interface INotificationDispatcher
{
    Task<int> DispatchDueAsync(CancellationToken cancellationToken = default);
}
