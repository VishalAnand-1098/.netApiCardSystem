using System.Net;
using InviteCraft.Domain.Common;
using InviteCraft.Domain.Entities.Identity;
using InviteCraft.Domain.Enums;

namespace InviteCraft.Domain.Entities.Platform;

/// <summary>Outbox-style notification: written in the same transaction as the business change, sent by a worker.</summary>
public class Notification : CreationAuditedEntity
{
    public Guid UserId { get; set; }
    public NotificationChannel Channel { get; set; }
    /// <summary>Event type, e.g. "EmailVerification", "OrderPaid".</summary>
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    /// <summary>JSON payload for templates/deep links.</summary>
    public string? Data { get; set; }
    public NotificationStatus Status { get; set; } = NotificationStatus.Pending;
    public int Attempts { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }

    public User User { get; set; } = null!;
}

public class AuditLog : CreationAuditedEntity
{
    public Guid? UserId { get; set; }
    /// <summary>e.g. "Create", "Update", "Delete", "Login", "LoginFailed", "PaymentVerified", "Download".</summary>
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public IPAddress? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? CorrelationId { get; set; }

    public User? User { get; set; }
}

/// <summary>Admin-editable configuration (tax rate, contact details, watermark textâ€¦), keyed by name.</summary>
public class SiteSetting
{
    public string Key { get; set; } = string.Empty;
    /// <summary>JSON value.</summary>
    public string Value { get; set; } = "null";
    public string? Description { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
