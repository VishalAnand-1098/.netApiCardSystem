using InviteCraft.Application.Common.Models;

namespace InviteCraft.Application.Features.Payments;

public static class PaymentErrors
{
    /// <summary>Also returned for someone else's order, so ids cannot be probed.</summary>
    public static readonly Error OrderNotFound = Error.NotFound("Order.NotFound", "Order not found.");

    public static readonly Error PackageNotFound = Error.Validation("Checkout.PackageNotFound", "That package is not available for this design.", "packageCode");

    public static readonly Error FreePackage = Error.Validation("Checkout.FreePackage",
        "Free designs need no purchase — you can download them straight away.", "packageCode");

    public static Error PackageNotAvailable(string reason) => Error.Conflict("Checkout.PackageNotAvailable", reason);

    public static readonly Error TooManyPending = Error.Conflict("Checkout.TooManyPending",
        "You have several unpaid orders open. Finish or wait for them to expire before starting another.");

    public static readonly Error KeyReused = Error.Conflict("Checkout.IdempotencyKeyReused",
        "This checkout was changed or has finished. Reload the page to start again.");

    public static readonly Error OrderClosed = Error.Conflict("Order.Closed",
        "This order has expired or was cancelled. Start a new checkout from your invitation.");

    public static readonly Error GatewayUnavailable = Error.Failure("Payment.GatewayUnavailable",
        "The payment provider is not reachable right now. Please try again in a minute — you have not been charged.");

    public static readonly Error InvalidSignature = Error.Validation("Payment.InvalidSignature",
        "We could not verify this payment. If money was deducted, it will be matched to your order automatically within a few minutes.");

    public static readonly Error GatewayOrderMismatch = Error.Validation("Payment.OrderMismatch", "This payment does not belong to this order.", "gatewayOrderId");

    public static readonly Error AmountMismatch = Error.Conflict("Payment.AmountMismatch",
        "The amount paid does not match the order total. Our team has been notified and will resolve this — please contact support.");

    public static Error Coupon(string code, string message) => Error.Validation(code, message, "couponCode");

    public static readonly Error WebhookSignature = Error.Validation("Webhook.InvalidSignature", "Invalid webhook signature.");
    public static readonly Error WebhookMalformed = Error.Validation("Webhook.Malformed", "The webhook payload could not be read.");
    public static readonly Error WebhookFailed = Error.Failure("Webhook.ProcessingFailed", "The event could not be processed yet; it will be retried.");

    public static readonly Error NotAvailable = Error.NotFound("Payment.NotAvailable", "Not found.");
}
