using InviteCraft.Application.Features.Payments;
using InviteCraft.Domain.Entities.Commerce;
using InviteCraft.Domain.Enums;

namespace InviteCraft.UnitTests.Payments;

public class PricingRulesTests
{
    [Fact]
    public void Tax_is_the_gst_portion_inside_a_tax_inclusive_total()
    {
        var price = PricingRules.Breakdown(299m, 0m, 18m);

        price.Subtotal.ShouldBe(299m);
        price.Total.ShouldBe(299m);
        price.Tax.ShouldBe(45.61m); // 299 × 18 / 118
    }

    [Fact]
    public void A_discount_reduces_the_total_and_the_tax_is_taken_from_what_is_actually_paid()
    {
        var price = PricingRules.Breakdown(299m, 29.9m, 18m);

        price.Discount.ShouldBe(29.9m);
        price.Total.ShouldBe(269.1m);
        price.Subtotal.ShouldBe(price.Total + price.Discount); // the database requires total = subtotal - discount
        price.Tax.ShouldBe(41.05m);
    }

    [Theory]
    [InlineData(100, 500, 100, 0)]   // discount can never exceed the price
    [InlineData(100, -5, 100, 100)]  // nor be negative (it is ignored)
    [InlineData(-20, 0, 0, 0)]       // an upgrade credit larger than the price is not a negative bill
    public void Amounts_are_clamped_so_an_order_can_never_have_a_negative_total(double payable, double discount, double subtotal, double total)
    {
        var price = PricingRules.Breakdown((decimal)payable, (decimal)discount, 18m);

        price.Subtotal.ShouldBe((decimal)subtotal);
        price.Total.ShouldBe((decimal)total);
        price.Total.ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void A_zero_gst_rate_means_no_tax()
    {
        PricingRules.Breakdown(100m, 0m, 0m).Tax.ShouldBe(0m);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 100)]
    [InlineData(269.1, 26910)]
    [InlineData(549, 54900)]
    [InlineData(0.01, 1)]
    public void Money_is_converted_to_whole_paise_exactly(double rupees, long paise) =>
        PricingRules.ToPaise((decimal)rupees).ShouldBe(paise);

    [Fact]
    public void Rounding_is_to_two_decimals_away_from_zero()
    {
        PricingRules.Round(1.005m).ShouldBe(1.01m);
        PricingRules.Round(2.994m).ShouldBe(2.99m);
    }
}

public class CouponRulesTests
{
    private static readonly DateTimeOffset Now = PaymentWorld.Now;
    private readonly PaymentWorld _world = new();

    private Coupon Coupon(Action<Coupon>? configure = null)
    {
        var coupon = _world.Coupon();
        configure?.Invoke(coupon);
        return coupon;
    }

    private static InviteCraft.Application.Common.Models.Result<decimal> Evaluate(Coupon? coupon, decimal subtotal = 299m, int used = 0, Guid? package = null, Guid? category = null) =>
        CouponRules.Evaluate(coupon, Now, subtotal, package ?? Guid.NewGuid(), category ?? Guid.NewGuid(), used);

    [Fact]
    public void A_percentage_coupon_takes_that_share_off_the_payable_price()
    {
        Evaluate(Coupon(), 299m).Value.ShouldBe(29.9m);
    }

    [Fact]
    public void A_fixed_coupon_takes_a_fixed_amount_but_never_more_than_the_price()
    {
        var fixed100 = Coupon(c => { c.DiscountType = DiscountType.FixedAmount; c.DiscountValue = 100; });

        Evaluate(fixed100, 299m).Value.ShouldBe(100m);
        Evaluate(fixed100, 60m).Value.ShouldBe(60m);
    }

    [Fact]
    public void The_maximum_discount_caps_a_percentage_coupon()
    {
        var capped = Coupon(c => { c.DiscountValue = 50; c.MaxDiscountAmount = 75; });

        Evaluate(capped, 549m).Value.ShouldBe(75m);
    }

    [Fact]
    public void A_hundred_percent_coupon_makes_the_order_free()
    {
        Evaluate(Coupon(c => c.DiscountValue = 100), 299m).Value.ShouldBe(299m);
    }

    [Theory]
    [InlineData("Coupon.Invalid")]
    public void Unknown_and_inactive_coupons_are_invalid(string code)
    {
        Evaluate(null).FirstError!.Code.ShouldBe(code);
        Evaluate(Coupon(c => c.IsActive = false)).FirstError!.Code.ShouldBe(code);
    }

    [Fact]
    public void A_coupon_outside_its_dates_is_refused_with_a_reason()
    {
        Evaluate(Coupon(c => c.StartsAt = Now.AddDays(1))).FirstError!.Code.ShouldBe("Coupon.NotStarted");
        Evaluate(Coupon(c => c.EndsAt = Now)).FirstError!.Code.ShouldBe("Coupon.Expired"); // the end instant itself is already over
        Evaluate(Coupon(c => { c.StartsAt = Now.AddDays(-1); c.EndsAt = Now.AddDays(1); })).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void The_usage_limit_counts_reservations_so_concurrent_checkouts_cannot_exceed_it()
    {
        Evaluate(Coupon(c => { c.UsageLimit = 10; c.UsedCount = 10; })).FirstError!.Code.ShouldBe("Coupon.Exhausted");
        Evaluate(Coupon(c => { c.UsageLimit = 10; c.UsedCount = 9; })).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void The_per_customer_limit_is_enforced()
    {
        Evaluate(Coupon(), used: 1).FirstError!.Code.ShouldBe("Coupon.AlreadyUsed");
        Evaluate(Coupon(c => c.PerUserLimit = 3), used: 2).IsSuccess.ShouldBeTrue();
        Evaluate(Coupon(c => c.PerUserLimit = 3), used: 3).FirstError!.Message.ShouldContain("limit");
    }

    [Fact]
    public void The_minimum_order_amount_must_be_met_on_the_payable_price()
    {
        var minimum = Coupon(c => c.MinOrderAmount = 300);

        var result = Evaluate(minimum, 299m);

        result.FirstError!.Code.ShouldBe("Coupon.MinimumNotMet");
        result.FirstError.Message.ShouldContain("300");
        Evaluate(minimum, 300m).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Package_and_category_restrictions_apply_when_present()
    {
        var packageId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var restricted = Coupon(c => { c.ApplicablePackageIds = [packageId]; c.ApplicableCategoryIds = [categoryId]; });

        Evaluate(restricted, package: packageId, category: categoryId).IsSuccess.ShouldBeTrue();
        Evaluate(restricted, package: Guid.NewGuid(), category: categoryId).FirstError!.Code.ShouldBe("Coupon.NotApplicable");
        Evaluate(restricted, package: packageId, category: Guid.NewGuid()).FirstError!.Code.ShouldBe("Coupon.NotApplicable");
    }

    [Fact]
    public void A_coupon_that_would_discount_nothing_is_refused()
    {
        Evaluate(Coupon(), 0m).FirstError!.Code.ShouldBe("Coupon.NoDiscount");
    }

    [Fact]
    public void Coupon_errors_point_at_the_coupon_field()
    {
        Evaluate(null).FirstError!.Field.ShouldBe("couponCode");
    }
}
