namespace InviteCraft.Domain.Enums;

public enum OrderStatus
{
    Pending,
    AwaitingPayment,
    Paid,
    Failed,
    Cancelled,
    Expired,
    Refunded
}

public enum PaymentStatus
{
    Created,
    Authorized,
    Captured,
    Failed,
    Refunded
}

/// <summary>Which path confirmed the payment: checkout callback, webhook, or a reconciliation job.</summary>
public enum PaymentVerificationSource
{
    Checkout,
    Webhook,
    Reconciliation
}

public enum WebhookEventStatus
{
    Received,
    Processed,
    Ignored,
    Failed
}

public enum DiscountType
{
    Percentage,
    FixedAmount
}

public enum CouponRedemptionStatus
{
    Reserved,
    Redeemed,
    Released
}
