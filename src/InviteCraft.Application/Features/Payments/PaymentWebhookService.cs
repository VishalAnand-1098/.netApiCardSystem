using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Application.Common.Models;
using InviteCraft.Domain.Entities.Commerce;
using InviteCraft.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace InviteCraft.Application.Features.Payments;

public interface IPaymentWebhookService
{
    /// <summary>
    /// Handles one gateway webhook delivery. A bad signature is rejected before anything is stored; a valid event is recorded once
    /// (duplicates and gateway retries are no-ops) and dispatched to the payment processor. Failure means "retry later" (non-2xx).
    /// </summary>
    Task<Result> HandleAsync(string rawBody, string? signature, string? eventId, CancellationToken cancellationToken = default);
}

/// <summary>Reads gateway payment objects. Only whitelisted, non-personal fields are kept in <see cref="GatewayPayment.Raw"/>.</summary>
public static class GatewayPayments
{
    private static readonly string[] SafeFields =
        ["id", "order_id", "amount", "currency", "status", "method", "captured", "fee", "tax", "error_code", "error_description", "error_source", "error_reason", "created_at"];

    public static GatewayPayment Parse(JsonElement entity)
    {
        string? Text(string name) => entity.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        var raw = new Dictionary<string, JsonElement>();
        foreach (var name in SafeFields)
            if (entity.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null)
                raw[name] = value;

        return new GatewayPayment(
            Text("id") ?? string.Empty,
            Text("order_id") ?? string.Empty,
            Text("status") ?? string.Empty,
            entity.TryGetProperty("amount", out var amount) && amount.TryGetInt64(out var paise) ? paise : 0,
            Text("currency") ?? "INR",
            Text("method"), Text("error_code"), Text("error_description"),
            JsonSerializer.Serialize(raw));
    }
}

internal sealed class PaymentWebhookService(
    IPaymentGateway gateway,
    IWebhookEventRepository events,
    IOrderRepository orders,
    IPaymentProcessor processor,
    IUnitOfWork unitOfWork,
    IPersistenceConflicts conflicts,
    TimeProvider timeProvider,
    ILogger<PaymentWebhookService> logger) : IPaymentWebhookService
{
    private const int MaxStoredPayloadChars = 64 * 1024;

    public async Task<Result> HandleAsync(string rawBody, string? signature, string? eventId, CancellationToken cancellationToken = default)
    {
        // 1. Authenticate before parsing, storing or trusting anything.
        if (string.IsNullOrEmpty(signature) || !gateway.VerifyWebhookSignature(rawBody, signature))
        {
            logger.LogWarning("Rejected a {Provider} webhook with an invalid signature", gateway.Provider);
            return PaymentErrors.WebhookSignature;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(rawBody);
        }
        catch (JsonException)
        {
            return PaymentErrors.WebhookMalformed;
        }

        using (document)
        {
            var root = document.RootElement;
            var eventType = root.TryGetProperty("event", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
            if (string.IsNullOrWhiteSpace(eventType))
                return PaymentErrors.WebhookMalformed;

            // 2. Record the event once. The unique (provider, event id) index makes gateway retries and duplicate deliveries no-ops.
            var id = string.IsNullOrWhiteSpace(eventId) ? "h_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawBody))).ToLowerInvariant() : eventId.Trim();
            if (id.Length > 100)
                id = id[..100];

            var record = await events.FindAsync(gateway.Provider, id, cancellationToken);
            if (record is { Status: WebhookEventStatus.Processed or WebhookEventStatus.Ignored })
                return Result.Success();

            if (record is null)
            {
                record = new PaymentWebhookEvent
                {
                    Provider = gateway.Provider, EventId = id, EventType = eventType!, SignatureValid = true,
                    Payload = rawBody.Length > MaxStoredPayloadChars ? "{}" : rawBody, ReceivedAt = timeProvider.GetUtcNow(),
                };
                await events.AddAsync(record, cancellationToken);
                try
                {
                    await unitOfWork.SaveChangesAsync(cancellationToken);
                }
                catch (Exception ex) when (conflicts.IsConflict(ex))
                {
                    conflicts.DiscardChanges(); // the same event arrived twice at once: the other delivery handles it
                    return Result.Success();
                }
            }

            record.Attempts++;

            // 3. Dispatch.
            WebhookEventStatus status;
            string? error = null;
            try
            {
                (status, error) = await DispatchAsync(eventType!, root, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Webhook {EventType} ({EventId}) failed", eventType, id);
                status = WebhookEventStatus.Failed;
                error = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
                conflicts.DiscardChanges();
                record = await events.FindAsync(gateway.Provider, id, cancellationToken) ?? record;
                record.Attempts++;
            }

            record.Status = status;
            record.Error = error;
            record.ProcessedAt = status == WebhookEventStatus.Failed ? null : timeProvider.GetUtcNow();
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return status == WebhookEventStatus.Failed ? PaymentErrors.WebhookFailed : Result.Success();
        }
    }

    private async Task<(WebhookEventStatus Status, string? Error)> DispatchAsync(string eventType, JsonElement root, CancellationToken cancellationToken)
    {
        switch (eventType)
        {
            case "payment.captured" or "payment.authorized" or "order.paid":
            {
                if (!TryEntity(root, "payment", out var entity))
                    return (WebhookEventStatus.Ignored, "No payment in the event.");

                var payment = GatewayPayments.Parse(entity);
                var order = await orders.FindByGatewayOrderIdAsync(payment.OrderId, cancellationToken);
                if (order is null)
                    return (WebhookEventStatus.Ignored, "Unknown order.");

                if (payment.Status == "authorized")
                    payment = await gateway.CapturePaymentAsync(payment.Id, payment.AmountPaise, payment.Currency, cancellationToken);
                if (payment.Status != "captured")
                    return (WebhookEventStatus.Ignored, $"Payment is {payment.Status}.");

                var applied = await processor.ApplyCapturedAsync(order.Id, payment, PaymentVerificationSource.Webhook, null, cancellationToken);
                if (applied.IsSuccess)
                    return (WebhookEventStatus.Processed, applied.Value == PaymentOutcome.Flagged ? "Flagged for support." : null);

                // A mismatch is recorded and flagged by the processor: retrying cannot fix it, so the event counts as handled.
                return applied.FirstError == PaymentErrors.AmountMismatch
                    ? (WebhookEventStatus.Processed, "Amount mismatch flagged.")
                    : (WebhookEventStatus.Failed, applied.FirstError?.Message);
            }

            case "payment.failed":
            {
                if (!TryEntity(root, "payment", out var entity))
                    return (WebhookEventStatus.Ignored, "No payment in the event.");

                var payment = GatewayPayments.Parse(entity);
                var order = await orders.FindByGatewayOrderIdAsync(payment.OrderId, cancellationToken);
                if (order is null)
                    return (WebhookEventStatus.Ignored, "Unknown order.");

                var recorded = await processor.RecordFailureAsync(order.Id, payment, cancellationToken);
                return recorded.IsSuccess ? (WebhookEventStatus.Processed, null) : (WebhookEventStatus.Failed, recorded.FirstError?.Message);
            }

            case "refund.processed":
            {
                if (!TryEntity(root, "refund", out var entity))
                    return (WebhookEventStatus.Ignored, "No refund in the event.");

                var paymentId = entity.TryGetProperty("payment_id", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
                var amount = entity.TryGetProperty("amount", out var a) && a.TryGetInt64(out var paise) ? paise : 0;
                if (string.IsNullOrEmpty(paymentId))
                    return (WebhookEventStatus.Ignored, "No payment id.");

                var refunded = await processor.ApplyRefundAsync(paymentId, amount, cancellationToken);
                return refunded.IsSuccess ? (WebhookEventStatus.Processed, null) : (WebhookEventStatus.Failed, refunded.FirstError?.Message);
            }

            default:
                return (WebhookEventStatus.Ignored, "Event type not handled.");
        }
    }

    private static bool TryEntity(JsonElement root, string name, out JsonElement entity)
    {
        entity = default;
        return root.TryGetProperty("payload", out var payload) && payload.TryGetProperty(name, out var holder) && holder.TryGetProperty("entity", out entity);
    }
}

internal sealed class OrderExpiryService(
    IOrderRepository orders,
    IUnitOfWork unitOfWork,
    IPersistenceConflicts conflicts,
    IAuditLogger audit,
    TimeProvider timeProvider,
    ILogger<OrderExpiryService> logger) : IOrderExpiryService
{
    private const int BatchSize = 50;

    public async Task<int> ExpireDueAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var expired = 0;

        foreach (var id in await orders.GetExpiredPayableIdsAsync(now, BatchSize, cancellationToken))
        {
            var order = await orders.GetForProcessingAsync(id, cancellationToken);
            if (order is null || order.Status is not (OrderStatus.Pending or OrderStatus.AwaitingPayment))
                continue;

            order.Status = order.Payments.Any(p => p.Status == PaymentStatus.Failed) ? OrderStatus.Failed : OrderStatus.Expired;
            var redemption = await orders.GetRedemptionAsync(order.Id, cancellationToken);
            if (redemption is { Status: CouponRedemptionStatus.Reserved })
            {
                redemption.Status = CouponRedemptionStatus.Released;
                redemption.Coupon.UsedCount = Math.Max(0, redemption.Coupon.UsedCount - 1);
            }

            audit.Log("OrderExpired", nameof(Domain.Entities.Commerce.Order), order.Id.ToString(), order.UserId, new { order.OrderNumber });
            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
                expired++;
            }
            catch (Exception ex) when (conflicts.IsConflict(ex))
            {
                conflicts.DiscardChanges(); // paid (or cancelled) in the meantime: leave it alone
            }
        }

        if (expired > 0)
            logger.LogInformation("Expired {Count} unpaid order(s)", expired);
        return expired;
    }
}
