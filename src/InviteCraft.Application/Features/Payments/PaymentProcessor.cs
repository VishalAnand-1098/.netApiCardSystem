using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Application.Features.Notifications;
using InviteCraft.Application.Common.Models;
using InviteCraft.Application.Features.Invitations;
using InviteCraft.Application.Features.Templates;
using InviteCraft.Domain.Entities.Commerce;
using InviteCraft.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace InviteCraft.Application.Features.Payments;

public enum PaymentOutcome
{
    /// <summary>This call turned the order into a paid one and unlocked the invitation.</summary>
    Applied,

    /// <summary>The payment was already processed (checkout callback and webhook both arrived — the second is a no-op).</summary>
    AlreadyProcessed,

    /// <summary>Money was received that cannot be applied automatically (a second payment for a paid order); recorded for support.</summary>
    Flagged,

    /// <summary>Internal: the save lost a race, re-read and decide again.</summary>
    Retry,
}

/// <summary>
/// The single place where an order becomes paid, fails or is refunded. Both the checkout callback and the gateway webhook come here,
/// and the database makes the race safe: a unique index on the gateway payment id and a concurrency token on the order mean that
/// whichever path loses re-reads the order and finds the work already done.
/// </summary>
public interface IPaymentProcessor
{
    Task<Result<PaymentOutcome>> ApplyCapturedAsync(
        Guid orderId, GatewayPayment payment, PaymentVerificationSource source, string? signature, CancellationToken cancellationToken = default);

    Task<Result> RecordFailureAsync(Guid orderId, GatewayPayment payment, CancellationToken cancellationToken = default);

    /// <summary>A full refund returns the order to Refunded and takes the entitlements away; a partial one is only recorded.</summary>
    Task<Result> ApplyRefundAsync(string gatewayPaymentId, long refundedPaise, CancellationToken cancellationToken = default);

    /// <summary>Marks a not-yet-saved zero-value order as paid and unlocks its invitations (the caller saves).</summary>
    Task CompleteFreeOrderAsync(Order order, CancellationToken cancellationToken = default);
}

internal sealed class PaymentProcessor(
    IOrderRepository orders,
    IInvitationRepository invitations,
    ITemplateRepository templates,
    IUnitOfWork unitOfWork,
    IPersistenceConflicts conflicts,
    IAuditLogger audit,
    INotificationService notifications,
    TimeProvider timeProvider,
    ILogger<PaymentProcessor> logger) : IPaymentProcessor
{
    private const int MaxAttempts = 3;

    // ------------------------------------------------------------------ captured

    public async Task<Result<PaymentOutcome>> ApplyCapturedAsync(
        Guid orderId, GatewayPayment payment, PaymentVerificationSource source, string? signature, CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var order = await orders.GetForProcessingAsync(orderId, cancellationToken);
            if (order is null)
                return PaymentErrors.OrderNotFound;

            var recorded = order.Payments.FirstOrDefault(p => p.GatewayPaymentId == payment.Id);
            if (order.Status == OrderStatus.Paid && recorded is { Status: PaymentStatus.Captured })
                return PaymentOutcome.AlreadyProcessed;

            var now = timeProvider.GetUtcNow();

            // A second payment for an order that is already paid: keep the evidence, never apply it twice.
            if (order.Status == OrderStatus.Paid)
            {
                AddOrUpdatePayment(order, recorded, payment, source, signature, now, PaymentStatus.Captured, "DUPLICATE_PAYMENT", "The order was already paid; this payment needs a refund.");
                audit.Log("DuplicatePaymentFlagged", nameof(Order), order.Id.ToString(), order.UserId, new { payment.Id, payment.AmountPaise });
                var duplicate = await SaveAsync(order, PaymentOutcome.Flagged, attempt, cancellationToken);
                if (duplicate.IsFailure || duplicate.Value != PaymentOutcome.Retry)
                    return duplicate;
                continue;
            }

            // The captured amount must be exactly what we asked for: anything else is flagged and nothing is unlocked.
            if (payment.AmountPaise != PricingRules.ToPaise(order.TotalAmount) ||
                !string.Equals(payment.Currency, order.Currency, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(payment.OrderId, order.GatewayOrderId, StringComparison.Ordinal))
            {
                AddOrUpdatePayment(order, recorded, payment, source, signature, now, PaymentStatus.Captured, "AMOUNT_MISMATCH",
                    $"Gateway reported {payment.AmountPaise} {payment.Currency} for order {payment.OrderId}; expected {PricingRules.ToPaise(order.TotalAmount)} {order.Currency} for {order.GatewayOrderId}.");
                audit.Log("PaymentAmountMismatch", nameof(Order), order.Id.ToString(), order.UserId,
                    new { payment.Id, payment.AmountPaise, expected = PricingRules.ToPaise(order.TotalAmount) });
                logger.LogError("Payment {PaymentId} does not match order {OrderNumber}: got {Got}, expected {Expected}",
                    payment.Id, order.OrderNumber, payment.AmountPaise, PricingRules.ToPaise(order.TotalAmount));
                var saved = await SaveAsync(order, PaymentOutcome.Flagged, attempt, cancellationToken);
                if (saved.IsSuccess && saved.Value == PaymentOutcome.Retry)
                    continue;
                return saved.IsFailure ? saved : PaymentErrors.AmountMismatch;
            }

            AddOrUpdatePayment(order, recorded, payment, source, signature, now, PaymentStatus.Captured, errorCode: null, errorDescription: null);
            order.Status = OrderStatus.Paid;
            order.PaidAt = now;

            await UnlockItemsAsync(order, now, cancellationToken);
            await RedeemCouponAsync(order, now, cancellationToken);

            audit.Log("OrderPaid", nameof(Order), order.Id.ToString(), order.UserId,
                new { order.OrderNumber, order.TotalAmount, payment = payment.Id, source = source.ToString(), payment.Method });
            notifications.OrderPaid(order); // saved together with the Paid transition: exactly once, whichever racer wins

            var result = await SaveAsync(order, PaymentOutcome.Applied, attempt, cancellationToken);
            if (result.IsFailure)
                return result;
            if (result.Value == PaymentOutcome.Retry)
                continue;

            foreach (var item in order.Items)
                await IncrementSalesQuietlyAsync(item.TemplateId, cancellationToken);
            return PaymentOutcome.Applied;
        }

        return PaymentErrors.WebhookFailed;
    }

    // ------------------------------------------------------------------ failed

    public async Task<Result> RecordFailureAsync(Guid orderId, GatewayPayment payment, CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var order = await orders.GetForProcessingAsync(orderId, cancellationToken);
            if (order is null)
                return PaymentErrors.OrderNotFound;

            // A failure report never downgrades a payment that already succeeded (events can arrive out of order).
            if (order.Payments.Any(p => p.GatewayPaymentId == payment.Id && p.Status != PaymentStatus.Failed))
                return Result.Success();

            var existing = order.Payments.FirstOrDefault(p => p.GatewayPaymentId == payment.Id);
            AddOrUpdatePayment(order, existing, payment, PaymentVerificationSource.Webhook, null, timeProvider.GetUtcNow(), PaymentStatus.Failed,
                payment.ErrorCode ?? "PAYMENT_FAILED", payment.ErrorDescription);
            audit.Log("PaymentFailed", nameof(Order), order.Id.ToString(), order.UserId, new { payment.Id, payment.ErrorCode });

            var result = await SaveAsync(order, PaymentOutcome.Applied, attempt, cancellationToken);
            if (result.IsFailure)
                return Result.Failure(result.Errors);
            if (result.Value != PaymentOutcome.Retry)
                return Result.Success();
        }

        return PaymentErrors.WebhookFailed;
    }

    // ------------------------------------------------------------------ refunds

    public async Task<Result> ApplyRefundAsync(string gatewayPaymentId, long refundedPaise, CancellationToken cancellationToken = default)
    {
        var payment = await orders.FindPaymentAsync(gatewayPaymentId, cancellationToken);
        if (payment is null)
            return Result.Success(); // not ours

        var order = await orders.GetForProcessingAsync(payment.OrderId, cancellationToken);
        if (order is null || payment.Status == PaymentStatus.Refunded)
            return Result.Success();

        var full = refundedPaise >= PricingRules.ToPaise(payment.Amount);
        audit.Log(full ? "OrderRefunded" : "OrderPartiallyRefunded", nameof(Order), order.Id.ToString(), order.UserId,
            new { order.OrderNumber, refundedPaise, payment = gatewayPaymentId });

        if (full)
        {
            payment.Status = PaymentStatus.Refunded;
            order.Status = OrderStatus.Refunded;
            foreach (var item in order.Items)
            {
                var invitation = await invitations.GetByIdAsync(item.InvitationId, cancellationToken);
                if (invitation?.UnlockedPackageId == item.PackageId)
                    invitation.Lock();
            }
            notifications.OrderRefunded(order);
        }

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (conflicts.IsConflict(ex))
        {
            conflicts.DiscardChanges(); // someone else already updated it: a refund is idempotent
        }

        return Result.Success();
    }

    // ------------------------------------------------------------------ free orders

    public async Task CompleteFreeOrderAsync(Order order, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        order.Status = OrderStatus.Paid;
        order.PaidAt = now;
        await UnlockItemsAsync(order, now, cancellationToken);
        await RedeemCouponAsync(order, now, cancellationToken);
        audit.Log("FreeOrderCompleted", nameof(Order), order.Id.ToString(), order.UserId, new { order.OrderNumber });
        notifications.OrderPaid(order);
    }

    // ------------------------------------------------------------------ helpers

    private static void AddOrUpdatePayment(
        Order order, Payment? existing, GatewayPayment gateway, PaymentVerificationSource source, string? signature, DateTimeOffset now,
        PaymentStatus status, string? errorCode, string? errorDescription)
    {
        var payment = existing;
        if (payment is null)
        {
            payment = new Payment { OrderId = order.Id, GatewayOrderId = order.GatewayOrderId ?? gateway.OrderId };
            order.Payments.Add(payment);
        }

        payment.GatewayPaymentId = gateway.Id;
        payment.GatewaySignature = signature;
        payment.Amount = PricingRules.Round(gateway.AmountPaise / 100m);
        payment.Currency = string.IsNullOrWhiteSpace(gateway.Currency) ? order.Currency : gateway.Currency.ToUpperInvariant();
        payment.Status = status;
        payment.Method = gateway.Method;
        payment.VerifiedVia = source;
        payment.ErrorCode = errorCode;
        payment.ErrorDescription = errorDescription is { Length: > 1000 } text ? text[..1000] : errorDescription;
        payment.RawResponse = gateway.Raw;
        payment.CapturedAt = status == PaymentStatus.Captured ? now : null;
        if (status == PaymentStatus.Captured && errorCode is null)
            payment.ErrorDescription = null;
    }

    private async Task UnlockItemsAsync(Order order, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var packages = await templates.GetActivePackagesAsync(cancellationToken);
        foreach (var item in order.Items)
        {
            var package = packages.FirstOrDefault(p => p.Id == item.PackageId);
            var invitation = await invitations.GetByIdAsync(item.InvitationId, cancellationToken);
            if (package is null || invitation is null)
            {
                logger.LogWarning("Order {OrderNumber}: cannot unlock invitation {InvitationId} (package or invitation missing)", order.OrderNumber, item.InvitationId);
                continue;
            }

            // A late payment for a lower package must not take away what a higher purchase already unlocked.
            var current = packages.FirstOrDefault(p => p.Id == invitation.UnlockedPackageId);
            if (current is null || current.SortOrder <= package.SortOrder)
                invitation.Unlock(package, now);
        }
    }

    private async Task RedeemCouponAsync(Order order, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (order.CouponId is null)
            return;
        var redemption = await orders.GetRedemptionAsync(order.Id, cancellationToken);
        if (redemption is { Status: CouponRedemptionStatus.Reserved })
        {
            redemption.Status = CouponRedemptionStatus.Redeemed;
            redemption.RedeemedAt = now;
        }
    }

    private async Task IncrementSalesQuietlyAsync(Guid templateId, CancellationToken cancellationToken)
    {
        try
        {
            await templates.IncrementSalesCountAsync(templateId, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not increment the sales counter of template {TemplateId}", templateId);
        }
    }

    /// <summary>Saves; a lost race (concurrency token or unique index) discards everything so the caller re-reads and re-decides.</summary>
    private async Task<Result<PaymentOutcome>> SaveAsync(Order order, PaymentOutcome success, int attempt, CancellationToken cancellationToken)
    {
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return success;
        }
        catch (Exception ex) when (conflicts.IsConflict(ex))
        {
            conflicts.DiscardChanges();
            logger.LogInformation("Order {OrderNumber}: lost a race while recording a payment (attempt {Attempt}); re-reading", order.OrderNumber, attempt);
            return attempt < MaxAttempts ? PaymentOutcome.Retry : PaymentErrors.WebhookFailed;
        }
    }
}
