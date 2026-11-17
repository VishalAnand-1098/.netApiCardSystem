using InviteCraft.Domain.Common;
using InviteCraft.Domain.Enums;

namespace InviteCraft.Domain.Entities.Commerce;

/// <summary>
/// A gateway payment attempt. <see cref="GatewayPaymentId"/> is unique in the database, which is the
/// final guard against processing the same payment twice (checkout callback vs webhook race).
/// No card data is ever stored.
/// </summary>
public class Payment : AuditableEntity
{
    public const string RazorpayProvider = "Razorpay";

    public Guid OrderId { get; set; }
    public string Provider { get; set; } = RazorpayProvider;
    public string GatewayOrderId { get; set; } = string.Empty;
    public string? GatewayPaymentId { get; set; }
    public string? GatewaySignature { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = Currencies.Inr;
    public PaymentStatus Status { get; set; } = PaymentStatus.Created;
    /// <summary>upi / card / netbanking / wallet (as reported by the gateway).</summary>
    public string? Method { get; set; }
    public PaymentVerificationSource? VerifiedVia { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorDescription { get; set; }
    /// <summary>Sanitised gateway response (JSON) for support and reconciliation.</summary>
    public string? RawResponse { get; set; }
    public DateTimeOffset? CapturedAt { get; set; }

    public uint Version { get; set; }

    public Order Order { get; set; } = null!;
}

/// <summary>Every received gateway webhook, deduplicated by (Provider, EventId).</summary>
public class PaymentWebhookEvent : Entity
{
    public string Provider { get; set; } = Payment.RazorpayProvider;
    public string EventId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public bool SignatureValid { get; set; }
    public string Payload { get; set; } = "{}";
    public WebhookEventStatus Status { get; set; } = WebhookEventStatus.Received;
    public int Attempts { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
}
