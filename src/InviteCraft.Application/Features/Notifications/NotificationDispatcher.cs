using System.Text.Json;
using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Application.Features.Auth;
using InviteCraft.Application.Features.Payments;
using InviteCraft.Application.Features.Sharing;
using InviteCraft.Domain.Entities.Platform;
using InviteCraft.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InviteCraft.Application.Features.Notifications;

/// <summary>
/// Sends the pending email notifications. Each email is composed at send time from the current order,
/// so a mail that goes out after a later change still tells the truth.
/// </summary>
internal sealed class NotificationDispatcher(
    INotificationsRepository notifications,
    IOrderRepository orders,
    IUserRepository users,
    IEmailSender email,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    IOptions<NotificationSettings> options,
    IOptions<SharingSettings> sharing,
    ILogger<NotificationDispatcher> logger) : INotificationDispatcher
{
    public async Task<int> DispatchDueAsync(CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var pending = await notifications.GetPendingEmailAsync(settings.BatchSize, settings.MaxAttempts, cancellationToken);
        if (pending.Count == 0)
            return 0;

        var sent = 0;
        foreach (var notification in pending)
        {
            notification.Attempts++;
            try
            {
                var message = await ComposeAsync(notification, cancellationToken);
                if (message is null)
                {
                    notification.Status = NotificationStatus.Failed;
                    notification.Error = "The notification could not be composed (the order or customer no longer exists).";
                    continue;
                }

                await email.SendAsync(message, cancellationToken);
                notification.Status = NotificationStatus.Sent;
                notification.SentAt = timeProvider.GetUtcNow();
                notification.Error = null;
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                notification.Error = ex.Message is { Length: > 500 } text ? text[..500] : ex.Message;
                if (notification.Attempts >= settings.MaxAttempts)
                    notification.Status = NotificationStatus.Failed;
                logger.LogWarning(ex, "Notification email {NotificationId} ({Type}) attempt {Attempt} failed", notification.Id, notification.Type, notification.Attempts);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return sent;
    }

    private async Task<EmailMessage?> ComposeAsync(Notification notification, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(notification.UserId, cancellationToken);
        if (user is null)
            return null;

        switch (notification.Type)
        {
            case NotificationTypes.OrderPaid:
            case NotificationTypes.OrderRefunded:
            {
                var orderId = OrderIdFrom(notification.Data);
                var order = orderId is null ? null : await orders.GetFullAsync(orderId.Value, cancellationToken);
                if (order is null)
                    return null;
                return notification.Type == NotificationTypes.OrderPaid
                    ? OrderEmails.Receipt(order, user, sharing.Value.PublicOrigin)
                    : OrderEmails.Refunded(order, user, sharing.Value.PublicOrigin);
            }
            default:
                // A plain fallback keeps unknown types from poisoning the queue.
                return new EmailMessage(user.Email, user.FullName, notification.Title, $"<p>{notification.Body}</p>", notification.Body);
        }
    }

    private static Guid? OrderIdFrom(string? data)
    {
        if (string.IsNullOrWhiteSpace(data))
            return null;
        try
        {
            using var document = JsonDocument.Parse(data);
            return document.RootElement.TryGetProperty("orderId", out var id) && id.TryGetGuid(out var guid) ? guid : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
