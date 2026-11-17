using System.Text.Json;
using InviteCraft.Application.Common.Models;
using InviteCraft.Domain.Entities.Commerce;
using InviteCraft.Domain.Entities.Platform;
using InviteCraft.Domain.Enums;

namespace InviteCraft.Application.Features.Notifications;

internal sealed class NotificationService(INotificationsRepository notifications, TimeProvider timeProvider) : INotificationService
{
    public Task<PagedResult<NotificationDto>> ListAsync(Guid userId, NotificationQuery query, CancellationToken cancellationToken = default) =>
        notifications.ListInAppAsync(userId, query, cancellationToken);

    public Task<int> CountUnreadAsync(Guid userId, CancellationToken cancellationToken = default) =>
        notifications.CountUnreadAsync(userId, cancellationToken);

    public Task<int> MarkReadAsync(Guid userId, MarkReadRequest request, CancellationToken cancellationToken = default) =>
        notifications.MarkReadAsync(userId, request.Ids is { Count: > 0 } ? request.Ids : null, timeProvider.GetUtcNow(), cancellationToken);

    // ------------------------------------------------------------------ enqueue

    public void OrderPaid(Order order) => Enqueue(
        order,
        NotificationTypes.OrderPaid,
        inAppTitle: $"Payment received · {order.OrderNumber}",
        inAppBody: $"₹{order.TotalAmount:0.00} received — your invitation is unlocked and ready to download.",
        emailSubject: $"Your receipt · {order.OrderNumber} · InviteCraft");

    public void OrderRefunded(Order order) => Enqueue(
        order,
        NotificationTypes.OrderRefunded,
        inAppTitle: $"Order refunded · {order.OrderNumber}",
        inAppBody: $"₹{order.TotalAmount:0.00} is on its way back to your payment method. The invitation is locked again.",
        emailSubject: $"Your refund · {order.OrderNumber} · InviteCraft");

    private void Enqueue(Order order, string type, string inAppTitle, string inAppBody, string emailSubject)
    {
        var now = timeProvider.GetUtcNow();
        var data = JsonSerializer.Serialize(new { orderId = order.Id, orderNumber = order.OrderNumber });

        // The in-app row is "delivered" the moment it exists; ReadAt tracks whether it was seen.
        notifications.Add(new Notification
        {
            UserId = order.UserId, Channel = NotificationChannel.InApp, Type = type,
            Title = inAppTitle, Body = inAppBody, Data = data,
            Status = NotificationStatus.Sent, SentAt = now,
        });

        // The email is composed and sent by the dispatcher (it reloads the order, so the mail shows the final state).
        notifications.Add(new Notification
        {
            UserId = order.UserId, Channel = NotificationChannel.Email, Type = type,
            Title = emailSubject, Body = inAppBody, Data = data,
            Status = NotificationStatus.Pending,
        });
    }
}
