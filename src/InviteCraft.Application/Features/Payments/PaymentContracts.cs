using InviteCraft.Application.Common.Models;
using InviteCraft.Domain.Entities.Commerce;
using InviteCraft.Domain.Enums;

namespace InviteCraft.Application.Features.Payments;

// ------------------------------------------------------------------ requests

/// <param name="IdempotencyKey">Client-generated per checkout attempt; repeating a request with the same key returns the same order instead of creating another.</param>
public sealed record CreateOrderRequest(Guid InvitationId, string PackageCode, string? CouponCode, string IdempotencyKey);

/// <summary>What the Razorpay Checkout callback hands back after a successful payment (the three fields that prove it).</summary>
public sealed record VerifyPaymentRequest(Guid OrderId, string GatewayOrderId, string PaymentId, string Signature);

public sealed record ValidateCouponRequest(Guid InvitationId, string PackageCode, string Code);

/// <summary>Development only (simulated provider): finishes a payment the way the gateway's checkout would.</summary>
public sealed record SimulatePaymentRequest(Guid OrderId, bool Success = true, bool DeliverWebhook = false);

// ------------------------------------------------------------------ responses

public sealed record CheckoutPackageDto(
    string Code,
    string Name,
    string? Description,
    IReadOnlyList<string> Features,
    IReadOnlyList<string> Formats,
    int MaxDpi,
    int? MaxDownloads,
    int? EditWindowDays,
    bool RemovesWatermark,
    bool IsRecommended,
    decimal ListPrice,
    decimal? CompareAtPrice,
    /// <summary>What the customer already paid for this invitation (an upgrade is charged the difference).</summary>
    decimal UpgradeCredit,
    decimal Payable,
    bool Available,
    string? UnavailableReason);

public sealed record CheckoutOptionsDto(
    Guid InvitationId,
    string InvitationTitle,
    string TemplateName,
    string? ThumbnailUrl,
    string Currency,
    decimal GstRatePercent,
    string? CurrentPackageCode,
    string? RecommendedPackageCode,
    IReadOnlyList<CheckoutPackageDto> Packages);

public sealed record CouponCheckDto(bool Valid, string? Code, decimal Discount, decimal Total, string? Message);

public sealed record PaymentInitDto(
    string Provider,
    string KeyId,
    string GatewayOrderId,
    long AmountPaise,
    string Currency,
    string BusinessName,
    string Description,
    string ThemeColor,
    PaymentPrefillDto Prefill);

public sealed record PaymentPrefillDto(string Name, string Email, string? Contact);

public sealed record OrderItemDto(Guid InvitationId, string InvitationTitle, string TemplateName, string PackageName, decimal UnitPrice, decimal LineTotal);

public sealed record OrderPaymentDto(string? PaymentId, PaymentStatus Status, string? Method, decimal Amount, DateTimeOffset? CapturedAt, string? ErrorDescription);

public sealed record OrderDto(
    Guid Id,
    string OrderNumber,
    OrderStatus Status,
    string Currency,
    decimal Subtotal,
    decimal DiscountAmount,
    /// <summary>GST included in <see cref="TotalAmount"/> (prices shown to customers are tax-inclusive).</summary>
    decimal TaxAmount,
    decimal TotalAmount,
    string? CouponCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PaidAt,
    DateTimeOffset? ExpiresAt,
    IReadOnlyList<OrderItemDto> Items,
    IReadOnlyList<OrderPaymentDto> Payments);

/// <param name="Payment">Null for free orders and orders that are already paid; otherwise what the browser needs to open the gateway's checkout.</param>
public sealed record CheckoutSessionDto(OrderDto Order, PaymentInitDto? Payment);

/// <summary>Development only.</summary>
public sealed record SimulatedPaymentDto(string GatewayOrderId, string PaymentId, string Signature);

// ------------------------------------------------------------------ gateway boundary

public sealed record GatewayOrderRequest(string Receipt, long AmountPaise, string Currency, IReadOnlyDictionary<string, string> Notes);

public sealed record GatewayOrder(string Id, long AmountPaise);

/// <param name="Status">The gateway's own word: "created", "authorized", "captured", "failed", "refunded".</param>
public sealed record GatewayPayment(
    string Id, string OrderId, string Status, long AmountPaise, string Currency, string? Method, string? ErrorCode, string? ErrorDescription, string Raw);

public sealed record GatewayRefund(string Id, long AmountPaise, string Status);

/// <summary>Talks to the payment provider (Razorpay in production, a signed in-process simulation in development).</summary>
public interface IPaymentGateway
{
    string Provider { get; }
    string PublicKeyId { get; }

    Task<GatewayOrder> CreateOrderAsync(GatewayOrderRequest request, CancellationToken cancellationToken = default);

    /// <summary>Null when the provider does not know the payment.</summary>
    Task<GatewayPayment?> GetPaymentAsync(string paymentId, CancellationToken cancellationToken = default);

    /// <summary>Captures an authorised payment (needed when the account does not auto-capture).</summary>
    Task<GatewayPayment> CapturePaymentAsync(string paymentId, long amountPaise, string currency, CancellationToken cancellationToken = default);

    /// <summary>Refunds (part of) a captured payment back to the customer's original method. Throws when the provider refuses.</summary>
    Task<GatewayRefund> RefundPaymentAsync(string paymentId, long amountPaise, string reason, CancellationToken cancellationToken = default);

    /// <summary>HMAC of "orderId|paymentId" with the key secret, compared in constant time.</summary>
    bool VerifyCheckoutSignature(string gatewayOrderId, string paymentId, string signature);

    /// <summary>HMAC of the raw request body with the webhook secret, compared in constant time.</summary>
    bool VerifyWebhookSignature(string rawBody, string signature);
}

/// <summary>Extra capabilities of the development gateway; the API exposes them only when this is the active provider.</summary>
public interface ISimulatedGateway
{
    SimulatedPaymentDto CreatePayment(string gatewayOrderId, long amountPaise, bool success);
    string SignWebhook(string rawBody);
}

// ------------------------------------------------------------------ persistence

public interface IOrderRepository : Common.Interfaces.IRepository<Order>
{
    /// <summary>Tracked, with items (+invitation), payments and the coupon redemption. Null when missing or someone else's.</summary>
    Task<Order?> GetOwnedAsync(Guid userId, Guid id, CancellationToken cancellationToken = default);

    Task<Order?> GetForProcessingAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The whole order (items with invitation titles, payments, coupon) regardless of owner — for emails.</summary>
    Task<Order?> GetFullAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Order?> FindByGatewayOrderIdAsync(string gatewayOrderId, CancellationToken cancellationToken = default);
    Task<Order?> FindByIdempotencyKeyAsync(Guid userId, string key, CancellationToken cancellationToken = default);
    Task<Payment?> FindPaymentAsync(string gatewayPaymentId, CancellationToken cancellationToken = default);

    /// <summary>An unexpired unpaid order for the same invitation and package (so a second tab does not open a second charge).</summary>
    Task<Order?> FindPayableAsync(Guid userId, Guid invitationId, Guid packageId, DateTimeOffset now, CancellationToken cancellationToken = default);

    Task AddRedemptionAsync(CouponRedemption redemption, CancellationToken cancellationToken = default);

    Task<int> CountPayableAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>The amount actually paid for the invitation's current package — credited when upgrading.</summary>
    Task<decimal> GetPaidAmountAsync(Guid invitationId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Guid>> GetExpiredPayableIdsAsync(DateTimeOffset now, int take, CancellationToken cancellationToken = default);
    Task<CouponRedemption?> GetRedemptionAsync(Guid orderId, CancellationToken cancellationToken = default);
}

public interface ICouponRepository : Common.Interfaces.IRepository<Coupon>
{
    /// <summary>Tracked. Case-insensitive; null for unknown, deleted or inactive coupons.</summary>
    Task<Coupon?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>How many times this customer has a Reserved or Redeemed redemption of the coupon.</summary>
    Task<int> CountUserRedemptionsAsync(Guid couponId, Guid userId, CancellationToken cancellationToken = default);
}

public interface IWebhookEventRepository
{
    Task<PaymentWebhookEvent?> FindAsync(string provider, string eventId, CancellationToken cancellationToken = default);
    Task AddAsync(PaymentWebhookEvent webhookEvent, CancellationToken cancellationToken = default);
}

/// <summary>
/// Tells the application layer that a save lost a race (concurrency token or unique index) without leaking EF Core / Npgsql types.
/// Payment code expects such races (checkout callback vs webhook) and resolves them by re-reading.
/// </summary>
public interface IPersistenceConflicts
{
    bool IsConflict(Exception exception);

    /// <summary>Forgets every tracked change so the caller can re-read the truth from the database.</summary>
    void DiscardChanges();
}

public interface IOrderExpiryService
{
    /// <summary>Expires unpaid orders past their deadline and releases their coupon reservations. Returns how many were expired.</summary>
    Task<int> ExpireDueAsync(CancellationToken cancellationToken = default);
}
