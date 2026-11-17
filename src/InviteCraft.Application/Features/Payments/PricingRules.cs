using System.Globalization;
using InviteCraft.Application.Common.Models;
using InviteCraft.Domain.Entities.Commerce;
using InviteCraft.Domain.Enums;

namespace InviteCraft.Application.Features.Payments;

public sealed record PriceBreakdown(decimal Subtotal, decimal Discount, decimal Total, decimal Tax);

/// <summary>Money maths in one place. Prices shown to customers include GST, so tax is the portion <em>inside</em> the total.</summary>
public static class PricingRules
{
    public const decimal DefaultGstRatePercent = 18m;

    /// <param name="payablePrice">List price minus any upgrade credit, never negative.</param>
    public static PriceBreakdown Breakdown(decimal payablePrice, decimal discount, decimal gstRatePercent)
    {
        var subtotal = Round(Math.Max(0, payablePrice));
        var applied = Round(Math.Clamp(discount, 0, subtotal));
        var total = subtotal - applied; // the database enforces total = subtotal - discount
        var tax = gstRatePercent > 0 ? Round(total * gstRatePercent / (100 + gstRatePercent)) : 0;
        return new PriceBreakdown(subtotal, applied, total, tax);
    }

    public static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    /// <summary>Whole paise (minor units) — what the gateway deals in. Exact for 2-decimal amounts.</summary>
    public static long ToPaise(decimal amount) => (long)Math.Round(amount * 100, 0, MidpointRounding.AwayFromZero);

    public static string Format(decimal amount) => "₹" + amount.ToString("N2", CultureInfo.GetCultureInfo("en-IN"));
}

/// <summary>Decides whether a coupon applies to a purchase and how much it takes off. Pure: callers supply the facts.</summary>
public static class CouponRules
{
    /// <param name="holdsReservation">
    /// The customer's own open order for this purchase already holds one reservation of this coupon. That order is reused (or replaced) by the
    /// checkout, so its reservation must not count against the coupon's total limit when the customer applies the same code again.
    /// </param>
    public static Result<decimal> Evaluate(
        Coupon? coupon, DateTimeOffset now, decimal subtotal, Guid packageId, Guid categoryId, int userRedemptions, bool holdsReservation = false)
    {
        if (coupon is null || !coupon.IsActive)
            return PaymentErrors.Coupon("Coupon.Invalid", "This coupon code is not valid.");
        if (coupon.StartsAt is { } start && now < start)
            return PaymentErrors.Coupon("Coupon.NotStarted", "This coupon is not active yet.");
        if (coupon.EndsAt is { } end && now >= end)
            return PaymentErrors.Coupon("Coupon.Expired", "This coupon has expired.");
        if (coupon.UsageLimit is { } limit && coupon.UsedCount - (holdsReservation ? 1 : 0) >= limit)
            return PaymentErrors.Coupon("Coupon.Exhausted", "This coupon has been fully redeemed.");
        if (userRedemptions >= coupon.PerUserLimit)
            return PaymentErrors.Coupon("Coupon.AlreadyUsed", coupon.PerUserLimit == 1 ? "You have already used this coupon." : "You have reached the limit for this coupon.");
        // Applicability first: "needs ₹200" is a misleading answer for a package the coupon can never apply to.
        if (coupon.ApplicablePackageIds.Count > 0 && !coupon.ApplicablePackageIds.Contains(packageId))
            return PaymentErrors.Coupon("Coupon.NotApplicable", "This coupon does not apply to the selected package.");
        if (coupon.ApplicableCategoryIds.Count > 0 && !coupon.ApplicableCategoryIds.Contains(categoryId))
            return PaymentErrors.Coupon("Coupon.NotApplicable", "This coupon does not apply to this kind of design.");
        if (coupon.MinOrderAmount is { } minimum && subtotal < minimum)
            return PaymentErrors.Coupon("Coupon.MinimumNotMet", $"This coupon needs an order of at least {PricingRules.Format(minimum)}.");

        var discount = coupon.DiscountType == DiscountType.Percentage
            ? PricingRules.Round(subtotal * coupon.DiscountValue / 100)
            : coupon.DiscountValue;
        if (coupon.MaxDiscountAmount is { } cap)
            discount = Math.Min(discount, cap);
        discount = Math.Min(discount, subtotal);

        return discount > 0
            ? PricingRules.Round(discount)
            : PaymentErrors.Coupon("Coupon.NoDiscount", "This coupon gives no discount on the selected package.");
    }
}
