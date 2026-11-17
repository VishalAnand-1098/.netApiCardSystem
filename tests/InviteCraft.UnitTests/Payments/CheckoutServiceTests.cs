using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Application.Common.Models;
using InviteCraft.Application.Features.Auth;
using InviteCraft.Application.Features.Content;
using InviteCraft.Application.Features.Invitations;
using InviteCraft.Application.Features.Payments;
using InviteCraft.Application.Features.Templates;
using InviteCraft.Domain.Entities.Commerce;
using InviteCraft.Domain.Entities.Identity;
using InviteCraft.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace InviteCraft.UnitTests.Payments;

public class CheckoutServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly PaymentWorld _w = new();
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly ICouponRepository _coupons = Substitute.For<ICouponRepository>();
    private readonly IInvitationRepository _invitations = Substitute.For<IInvitationRepository>();
    private readonly ITemplateRepository _templates = Substitute.For<ITemplateRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IContentRepository _content = Substitute.For<IContentRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IPaymentGateway _gateway = Substitute.For<IPaymentGateway>();
    private readonly IPaymentProcessor _processor = Substitute.For<IPaymentProcessor>();
    private readonly IPersistenceConflicts _conflicts = Substitute.For<IPersistenceConflicts>();
    private readonly IPaymentWebhookService _webhooks = Substitute.For<IPaymentWebhookService>();
    private readonly IAuditLogger _audit = Substitute.For<IAuditLogger>();
    private readonly IFileStorage _storage = Substitute.For<IFileStorage>();
    private readonly CheckoutService _sut;
    private readonly User _user = new() { FullName = "Ananya Sharma", Email = "ananya@example.com", Phone = "+919876543210" };

    private Order? _saved;

    public CheckoutServiceTests()
    {
        _sut = Build(_gateway);

        _invitations.GetOwnedAsync(_w.UserId, _w.Invitation.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(_w.Invitation);
        _templates.GetAggregateAsync(_w.Template.Id, false, Arg.Any<CancellationToken>()).Returns(_w.Template);
        _templates.GetActivePackagesAsync(Arg.Any<CancellationToken>()).Returns(_w.Packages);
        _users.GetByIdAsync(_w.UserId, Arg.Any<CancellationToken>()).Returns(_user);
        _content.GetSettingJsonAsync("tax.gst", Arg.Any<CancellationToken>()).Returns("""{"rate":18,"pricesIncludeTax":true}""");
        _content.GetSettingJsonAsync("orders", Arg.Any<CancellationToken>()).Returns("""{"pendingExpiryMinutes":30}""");
        _gateway.Provider.Returns("Razorpay");
        _gateway.PublicKeyId.Returns("rzp_test_abc");
        _gateway.CreateOrderAsync(Arg.Any<GatewayOrderRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci => new GatewayOrder("order_RZP1", ci.Arg<GatewayOrderRequest>().AmountPaise));
        _orders.AddAsync(Arg.Do<Order>(o => _saved = o), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _orders.GetOwnedAsync(_w.UserId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_ => _saved);
    }

    private CheckoutService Build(IPaymentGateway gateway) => new(_orders, _coupons, _invitations, _templates, _users, _content, _unitOfWork, gateway,
        _processor, _conflicts, _webhooks, new TemplateMapper(_storage), Options.Create(new CheckoutSettings { MaxPendingOrders = 3 }), _audit, _w.Clock,
        NullLogger<CheckoutService>.Instance);

    private static CreateOrderRequest Request(string package = "PREMIUM", string? coupon = null, string key = "checkout-key-0001", Guid? invitation = null) =>
        new(invitation ?? Guid.Empty, package, coupon, key);

    private Task<Result<CheckoutSessionDto>> Create(string package = "PREMIUM", string? coupon = null, string key = "checkout-key-0001") =>
        _sut.CreateOrderAsync(_w.UserId, Request(package, coupon, key, _w.Invitation.Id), Ct);

    // ------------------------------------------------------------------ options

    [Fact]
    public async Task Options_list_the_packages_the_design_is_sold_in_with_prices_and_exclude_free_and_custom()
    {
        var options = (await _sut.GetOptionsAsync(_w.UserId, _w.Invitation.Id, Ct)).Value;

        options.Packages.Select(p => p.Code).ShouldBe(["BASIC", "PREMIUM", "LUXURY"]);
        options.Packages.Select(p => p.ListPrice).ShouldBe([149m, 299m, 549m]);
        options.Packages[0].CompareAtPrice.ShouldBe(199m);
        options.Packages.ShouldAllBe(p => p.Available && p.UpgradeCredit == 0 && p.Payable == p.ListPrice);
        options.GstRatePercent.ShouldBe(18m);
        options.RecommendedPackageCode.ShouldBe("PREMIUM");
        options.CurrentPackageCode.ShouldBeNull();
    }

    [Fact]
    public async Task A_design_without_price_rows_is_sold_at_the_packages_base_prices()
    {
        _w.Template.Pricing.Clear();

        var options = (await _sut.GetOptionsAsync(_w.UserId, _w.Invitation.Id, Ct)).Value;

        options.Packages.Select(p => (p.Code, p.ListPrice)).ShouldBe([("BASIC", 99m), ("PREMIUM", 249m), ("LUXURY", 499m)]);
    }

    [Fact]
    public async Task Price_rows_outside_their_validity_window_or_inactive_are_not_offered()
    {
        var premium = _w.Template.Pricing.Single(p => p.Package == _w.Premium);
        premium.ValidTo = PaymentWorld.Now.AddDays(-1);
        _w.Template.Pricing.Single(p => p.Package == _w.Luxury).IsActive = false;

        var options = (await _sut.GetOptionsAsync(_w.UserId, _w.Invitation.Id, Ct)).Value;

        options.Packages.Select(p => p.Code).ShouldBe(["BASIC"]);
    }

    [Fact]
    public async Task An_upgrade_is_credited_with_what_was_already_paid_and_lower_packages_are_unavailable()
    {
        _w.Invitation.UnlockedPackageId = _w.Basic.Id;
        _orders.GetPaidAmountAsync(_w.Invitation.Id, Arg.Any<CancellationToken>()).Returns(149m);

        var options = (await _sut.GetOptionsAsync(_w.UserId, _w.Invitation.Id, Ct)).Value;

        options.CurrentPackageCode.ShouldBe("BASIC");
        var basic = options.Packages.Single(p => p.Code == "BASIC");
        basic.Available.ShouldBeFalse();
        basic.UnavailableReason.ShouldBe("You already have the Basic package.");
        var premium = options.Packages.Single(p => p.Code == "PREMIUM");
        (premium.Available, premium.ListPrice, premium.UpgradeCredit, premium.Payable).ShouldBe((true, 299m, 149m, 150m));
        options.Packages.Single(p => p.Code == "LUXURY").Payable.ShouldBe(400m);
    }

    [Fact]
    public async Task Someone_elses_invitation_is_not_found()
    {
        (await _sut.GetOptionsAsync(Guid.NewGuid(), _w.Invitation.Id, Ct)).FirstError.ShouldBe(InvitationErrors.NotFound);
    }

    // ------------------------------------------------------------------ coupon validation

    [Fact]
    public async Task Re_applying_the_coupon_held_by_my_own_open_order_is_allowed_after_a_page_reload()
    {
        var coupon = _w.Coupon("WELCOME10");
        coupon.UsageLimit = 1;
        coupon.UsedCount = 1;                                   // the only use: reserved by my own open order
        _coupons.GetByCodeAsync("WELCOME10", Arg.Any<CancellationToken>()).Returns(coupon);
        _coupons.CountUserRedemptionsAsync(coupon.Id, _w.UserId, Arg.Any<CancellationToken>()).Returns(1);
        var open = new Order { CouponId = coupon.Id, Coupon = coupon, Status = OrderStatus.AwaitingPayment };
        _orders.FindPayableAsync(_w.UserId, _w.Invitation.Id, _w.Premium.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(open);

        var check = (await _sut.ValidateCouponAsync(_w.UserId, new ValidateCouponRequest(_w.Invitation.Id, "PREMIUM", "WELCOME10"), Ct)).Value;

        check.Valid.ShouldBeTrue(check.Message);
        check.Discount.ShouldBe(29.9m);
    }

    [Fact]
    public async Task Someone_elses_use_of_a_coupon_still_counts_and_a_used_up_coupon_is_reported_as_not_valid_rather_than_an_error()
    {
        var coupon = _w.Coupon("WELCOME10");
        coupon.UsageLimit = 1;
        coupon.UsedCount = 1; // used by another customer; I hold no reservation
        _coupons.GetByCodeAsync("WELCOME10", Arg.Any<CancellationToken>()).Returns(coupon);

        var result = await _sut.ValidateCouponAsync(_w.UserId, new ValidateCouponRequest(_w.Invitation.Id, "PREMIUM", "WELCOME10"), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Valid.ShouldBeFalse();
        result.Value.Message.ShouldContain("fully redeemed");
        result.Value.Total.ShouldBe(299m);
    }

    // ------------------------------------------------------------------ create order

    [Fact]
    public async Task Creating_an_order_prices_it_asks_the_gateway_in_paise_and_returns_what_the_browser_needs()
    {
        var session = (await Create()).Value;

        // The gateway was asked for exactly the amount in paise, with our order number as the receipt.
        await _gateway.Received(1).CreateOrderAsync(Arg.Is<GatewayOrderRequest>(r =>
            r.AmountPaise == 29900 && r.Currency == "INR" && r.Receipt == _saved!.OrderNumber && r.Notes["package"] == "PREMIUM"), Ct);

        _saved.ShouldNotBeNull();
        _saved.Status.ShouldBe(OrderStatus.AwaitingPayment);
        _saved.GatewayOrderId.ShouldBe("order_RZP1");
        (_saved.Subtotal, _saved.DiscountAmount, _saved.TaxAmount, _saved.TotalAmount).ShouldBe((299m, 0m, 45.61m, 299m));
        _saved.OrderNumber.ShouldMatch(@"^ICO-2026-[A-Z0-9]{6}$");
        _saved.ExpiresAt.ShouldBe(PaymentWorld.Now.AddMinutes(30));
        var item = _saved.Items.Single();
        (item.InvitationId, item.PackageId, item.UnitPrice, item.LineTotal, item.TemplateName, item.PackageName)
            .ShouldBe((_w.Invitation.Id, _w.Premium.Id, 299m, 299m, "Royal Maroon Wedding", "Premium"));
        await _unitOfWork.Received(1).SaveChangesAsync(Ct);

        session.Order.Status.ShouldBe(OrderStatus.AwaitingPayment);
        var payment = session.Payment!;
        (payment.Provider, payment.KeyId, payment.GatewayOrderId, payment.AmountPaise, payment.Currency).ShouldBe(("Razorpay", "rzp_test_abc", "order_RZP1", 29900L, "INR"));
        payment.Prefill.ShouldBe(new PaymentPrefillDto("Ananya Sharma", "ananya@example.com", "+919876543210"));
        payment.Description.ShouldBe("Royal Maroon Wedding · Premium");
    }

    [Fact]
    public async Task A_coupon_is_applied_to_the_payable_price_and_reserved_with_the_order()
    {
        var coupon = _w.Coupon("WELCOME10");
        _coupons.GetByCodeAsync("WELCOME10", Arg.Any<CancellationToken>()).Returns(coupon);
        CouponRedemption? redemption = null;
        await _orders.AddRedemptionAsync(Arg.Do<CouponRedemption>(r => redemption = r), Arg.Any<CancellationToken>());

        var session = (await Create(coupon: "WELCOME10")).Value;

        (_saved!.Subtotal, _saved.DiscountAmount, _saved.TotalAmount).ShouldBe((299m, 29.9m, 269.1m));
        _saved.CouponId.ShouldBe(coupon.Id);
        coupon.UsedCount.ShouldBe(1); // reserved: concurrent checkouts race on this row's concurrency token
        redemption!.Status.ShouldBe(CouponRedemptionStatus.Reserved);
        redemption.DiscountAmount.ShouldBe(29.9m);
        session.Payment!.AmountPaise.ShouldBe(26910);
        await _gateway.Received(1).CreateOrderAsync(Arg.Is<GatewayOrderRequest>(r => r.AmountPaise == 26910), Ct);
    }

    [Fact]
    public async Task An_unusable_coupon_stops_the_checkout_before_anything_is_created()
    {
        _coupons.GetByCodeAsync("NOPE", Arg.Any<CancellationToken>()).Returns((Coupon?)null);

        var result = await Create(coupon: "NOPE");

        result.FirstError!.Code.ShouldBe("Coupon.Invalid");
        result.FirstError.Field.ShouldBe("couponCode");
        await _gateway.DidNotReceiveWithAnyArgs().CreateOrderAsync(default!, default);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_coupon_that_makes_the_order_free_completes_it_without_the_gateway()
    {
        var coupon = _w.Coupon("FREEBIE", percent: 100);
        _coupons.GetByCodeAsync("FREEBIE", Arg.Any<CancellationToken>()).Returns(coupon);
        _processor.CompleteFreeOrderAsync(Arg.Do<Order>(o => o.Status = OrderStatus.Paid), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var session = (await Create(coupon: "FREEBIE")).Value;

        _saved!.TotalAmount.ShouldBe(0m);
        _saved.Status.ShouldBe(OrderStatus.Paid);
        _saved.GatewayOrderId.ShouldBeNull();
        session.Payment.ShouldBeNull();
        await _processor.Received(1).CompleteFreeOrderAsync(_saved, Ct);
        await _gateway.DidNotReceiveWithAnyArgs().CreateOrderAsync(default!, default);
    }

    [Fact]
    public async Task The_same_idempotency_key_returns_the_same_order_instead_of_creating_another()
    {
        await Create();
        _orders.FindByIdempotencyKeyAsync(_w.UserId, "checkout-key-0001", Arg.Any<CancellationToken>()).Returns(_saved);

        var again = (await Create()).Value;

        again.Order.Id.ShouldBe(_saved!.Id);
        again.Payment!.GatewayOrderId.ShouldBe("order_RZP1");
        await _gateway.Received(1).CreateOrderAsync(Arg.Any<GatewayOrderRequest>(), Ct);
        await _orders.Received(1).AddAsync(Arg.Any<Order>(), Ct);
    }

    [Fact]
    public async Task Reusing_a_key_for_a_different_purchase_is_a_conflict()
    {
        await Create();
        _orders.FindByIdempotencyKeyAsync(_w.UserId, "checkout-key-0001", Arg.Any<CancellationToken>()).Returns(_saved);

        (await Create(package: "LUXURY")).FirstError.ShouldBe(PaymentErrors.KeyReused);
        (await Create(coupon: "WELCOME10")).FirstError.ShouldBe(PaymentErrors.KeyReused);
    }

    [Fact]
    public async Task A_key_whose_order_has_expired_or_been_cancelled_cannot_be_paid_again()
    {
        await Create();
        _orders.FindByIdempotencyKeyAsync(_w.UserId, "checkout-key-0001", Arg.Any<CancellationToken>()).Returns(_saved);

        _w.Clock.Now = PaymentWorld.Now.AddMinutes(31);
        (await Create()).FirstError.ShouldBe(PaymentErrors.OrderClosed);

        _w.Clock.Now = PaymentWorld.Now;
        _saved!.Status = OrderStatus.Cancelled;
        (await Create()).FirstError.ShouldBe(PaymentErrors.OrderClosed);
    }

    [Fact]
    public async Task A_paid_order_found_by_key_is_returned_as_paid_without_payment_details()
    {
        await Create();
        _saved!.Status = OrderStatus.Paid;
        _orders.FindByIdempotencyKeyAsync(_w.UserId, "checkout-key-0001", Arg.Any<CancellationToken>()).Returns(_saved);

        var session = (await Create()).Value;

        session.Order.Status.ShouldBe(OrderStatus.Paid);
        session.Payment.ShouldBeNull();
    }

    [Fact]
    public async Task A_second_tab_with_a_new_key_gets_the_order_that_is_already_open_for_the_same_purchase()
    {
        await Create();
        _orders.FindPayableAsync(_w.UserId, _w.Invitation.Id, _w.Premium.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(_saved);

        var second = (await Create(key: "checkout-key-0002")).Value;

        second.Order.Id.ShouldBe(_saved!.Id);
        await _gateway.Received(1).CreateOrderAsync(Arg.Any<GatewayOrderRequest>(), Ct);
    }

    [Fact]
    public async Task An_open_order_with_different_terms_is_cancelled_and_its_coupon_returned_when_the_customer_changes_their_mind()
    {
        await Create();
        var coupon = _w.Coupon();
        coupon.UsedCount = 1;
        _orders.FindPayableAsync(_w.UserId, _w.Invitation.Id, _w.Premium.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(_saved);
        _orders.GetRedemptionAsync(_saved!.Id, Arg.Any<CancellationToken>())
            .Returns(new CouponRedemption { CouponId = coupon.Id, Coupon = coupon, OrderId = _saved.Id, Status = CouponRedemptionStatus.Reserved });
        var first = _saved;
        _coupons.GetByCodeAsync("WELCOME10", Arg.Any<CancellationToken>()).Returns(_w.Coupon());

        await Create(coupon: "WELCOME10", key: "checkout-key-0002"); // a different coupon on the same purchase

        first.Status.ShouldBe(OrderStatus.Cancelled);
        first.CancelledAt.ShouldBe(PaymentWorld.Now);
        coupon.UsedCount.ShouldBe(0);
        await _gateway.Received(2).CreateOrderAsync(Arg.Any<GatewayOrderRequest>(), Ct);
    }

    [Fact]
    public async Task The_free_package_and_unknown_packages_cannot_be_bought()
    {
        (await Create("FREE")).FirstError.ShouldBe(PaymentErrors.FreePackage);
        (await Create("CUSTOM")).FirstError.ShouldBe(PaymentErrors.PackageNotFound);
        (await Create("PLATINUM")).FirstError.ShouldBe(PaymentErrors.PackageNotFound);
        await _gateway.DidNotReceiveWithAnyArgs().CreateOrderAsync(default!, default);
    }

    [Fact]
    public async Task A_package_the_invitation_already_has_or_exceeds_cannot_be_bought_again()
    {
        _w.Invitation.UnlockedPackageId = _w.Premium.Id;
        _orders.GetPaidAmountAsync(_w.Invitation.Id, Arg.Any<CancellationToken>()).Returns(299m);

        var result = await Create("BASIC");

        result.FirstError!.Code.ShouldBe("Checkout.PackageNotAvailable");
        result.FirstError.Message.ShouldContain("Premium");
        (await Create("LUXURY")).IsSuccess.ShouldBeTrue(); // an upgrade is fine
        _saved!.Subtotal.ShouldBe(250m); // 549 − 299 already paid
    }

    [Fact]
    public async Task Someone_elses_invitation_cannot_be_purchased()
    {
        var result = await _sut.CreateOrderAsync(Guid.NewGuid(), Request(invitation: _w.Invitation.Id), Ct);

        result.FirstError.ShouldBe(InvitationErrors.NotFound);
    }

    [Fact]
    public async Task Too_many_open_orders_are_refused()
    {
        _orders.CountPayableAsync(_w.UserId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(3);

        (await Create()).FirstError.ShouldBe(PaymentErrors.TooManyPending);
    }

    [Fact]
    public async Task When_the_gateway_is_down_nothing_is_saved_and_the_customer_is_told_they_were_not_charged()
    {
        _gateway.CreateOrderAsync(Arg.Any<GatewayOrderRequest>(), Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException("503"));

        var result = await Create();

        result.FirstError.ShouldBe(PaymentErrors.GatewayUnavailable);
        result.FirstError!.Message.ShouldContain("not been charged");
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Losing_a_race_on_the_coupon_counter_retries_from_fresh_data_and_reuses_the_gateway_order()
    {
        var coupon = _w.Coupon();
        _coupons.GetByCodeAsync("WELCOME10", Arg.Any<CancellationToken>()).Returns(coupon);
        var conflict = new InvalidOperationException("xmin changed");
        _conflicts.IsConflict(conflict).Returns(true);
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(
            Task.FromException<int>(conflict), Task.FromResult(1));

        var result = await Create(coupon: "WELCOME10");

        result.IsSuccess.ShouldBeTrue(result.FirstError?.Message);
        _conflicts.Received(1).DiscardChanges();
        await _gateway.Received(1).CreateOrderAsync(Arg.Any<GatewayOrderRequest>(), Ct); // same amount: the Razorpay order is reused
        await _unitOfWork.Received(2).SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task Other_failures_while_saving_are_not_swallowed()
    {
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("disk on fire"));

        await Should.ThrowAsync<InvalidOperationException>(() => Create());
    }

    // ------------------------------------------------------------------ verify

    private async Task<Order> OpenOrderAsync()
    {
        await Create();
        return _saved!;
    }

    private VerifyPaymentRequest Verify(Order order, string payment = "pay_1", string signature = "sig") => new(order.Id, order.GatewayOrderId!, payment, signature);

    private static GatewayPayment Captured(Order order, string id = "pay_1") =>
        new(id, order.GatewayOrderId!, "captured", PricingRules.ToPaise(order.TotalAmount), "INR", "upi", null, null, "{}");

    [Fact]
    public async Task A_valid_captured_payment_is_handed_to_the_processor_and_the_paid_order_returned()
    {
        var order = await OpenOrderAsync();
        _gateway.VerifyCheckoutSignature(order.GatewayOrderId!, "pay_1", "sig").Returns(true);
        _gateway.GetPaymentAsync("pay_1", Arg.Any<CancellationToken>()).Returns(Captured(order));
        _processor.ApplyCapturedAsync(order.Id, Arg.Any<GatewayPayment>(), PaymentVerificationSource.Checkout, "sig", Arg.Any<CancellationToken>())
            .Returns(ci => { order.Status = OrderStatus.Paid; return Task.FromResult<Result<PaymentOutcome>>(PaymentOutcome.Applied); });

        var dto = (await _sut.VerifyPaymentAsync(_w.UserId, Verify(order), Ct)).Value;

        dto.Status.ShouldBe(OrderStatus.Paid);
        await _processor.Received(1).ApplyCapturedAsync(order.Id, Arg.Is<GatewayPayment>(p => p.Id == "pay_1" && p.AmountPaise == 29900), PaymentVerificationSource.Checkout, "sig", Ct);
    }

    [Fact]
    public async Task A_forged_signature_is_rejected_and_nothing_is_applied()
    {
        var order = await OpenOrderAsync();
        _gateway.VerifyCheckoutSignature(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        var result = await _sut.VerifyPaymentAsync(_w.UserId, Verify(order, signature: "forged"), Ct);

        result.FirstError.ShouldBe(PaymentErrors.InvalidSignature);
        await _gateway.DidNotReceiveWithAnyArgs().GetPaymentAsync(default!, default);
        await _processor.DidNotReceiveWithAnyArgs().ApplyCapturedAsync(default, default!, default, default, default);
        _audit.Received().Log("PaymentSignatureRejected", nameof(Order), order.Id.ToString(), _w.UserId, Arg.Any<object?>());
    }

    [Fact]
    public async Task A_payment_for_a_different_gateway_order_is_rejected()
    {
        var order = await OpenOrderAsync();

        var result = await _sut.VerifyPaymentAsync(_w.UserId, new VerifyPaymentRequest(order.Id, "order_SOMEONE_ELSES", "pay_1", "sig"), Ct);

        result.FirstError.ShouldBe(PaymentErrors.GatewayOrderMismatch);
    }

    [Fact]
    public async Task Verifying_an_order_that_is_already_paid_is_a_harmless_repeat()
    {
        var order = await OpenOrderAsync();
        order.Status = OrderStatus.Paid;

        var dto = (await _sut.VerifyPaymentAsync(_w.UserId, Verify(order), Ct)).Value;

        dto.Status.ShouldBe(OrderStatus.Paid);
        _gateway.DidNotReceiveWithAnyArgs().VerifyCheckoutSignature(default!, default!, default!);
    }

    [Fact]
    public async Task An_authorised_payment_is_captured_before_it_is_applied()
    {
        var order = await OpenOrderAsync();
        _gateway.VerifyCheckoutSignature(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        _gateway.GetPaymentAsync("pay_1", Arg.Any<CancellationToken>()).Returns(Captured(order) with { Status = "authorized" });
        _gateway.CapturePaymentAsync("pay_1", 29900, "INR", Arg.Any<CancellationToken>()).Returns(Captured(order));
        _processor.ApplyCapturedAsync(order.Id, Arg.Any<GatewayPayment>(), Arg.Any<PaymentVerificationSource>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(PaymentOutcome.Applied);

        (await _sut.VerifyPaymentAsync(_w.UserId, Verify(order), Ct)).IsSuccess.ShouldBeTrue();

        await _gateway.Received(1).CapturePaymentAsync("pay_1", 29900, "INR", Ct);
    }

    [Theory]
    [InlineData("created")]
    [InlineData("failed")]
    public async Task A_payment_the_gateway_does_not_report_as_captured_leaves_the_order_unpaid(string status)
    {
        var order = await OpenOrderAsync();
        _gateway.VerifyCheckoutSignature(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        _gateway.GetPaymentAsync("pay_1", Arg.Any<CancellationToken>()).Returns(Captured(order) with { Status = status });

        var dto = (await _sut.VerifyPaymentAsync(_w.UserId, Verify(order), Ct)).Value;

        dto.Status.ShouldBe(OrderStatus.AwaitingPayment);
        await _processor.DidNotReceiveWithAnyArgs().ApplyCapturedAsync(default, default!, default, default, default);
    }

    [Fact]
    public async Task If_the_gateway_cannot_be_reached_the_webhook_is_left_to_finish_the_job()
    {
        var order = await OpenOrderAsync();
        _gateway.VerifyCheckoutSignature(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        _gateway.GetPaymentAsync("pay_1", Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException("timeout"));

        var result = await _sut.VerifyPaymentAsync(_w.UserId, Verify(order), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(OrderStatus.AwaitingPayment);
    }

    [Fact]
    public async Task An_amount_mismatch_from_the_processor_is_reported_to_the_customer()
    {
        var order = await OpenOrderAsync();
        _gateway.VerifyCheckoutSignature(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        _gateway.GetPaymentAsync("pay_1", Arg.Any<CancellationToken>()).Returns(Captured(order) with { AmountPaise = 100 });
        _processor.ApplyCapturedAsync(order.Id, Arg.Any<GatewayPayment>(), Arg.Any<PaymentVerificationSource>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<PaymentOutcome>(PaymentErrors.AmountMismatch));

        (await _sut.VerifyPaymentAsync(_w.UserId, Verify(order), Ct)).FirstError.ShouldBe(PaymentErrors.AmountMismatch);
    }

    [Fact]
    public async Task Another_customers_order_cannot_be_verified_read_or_cancelled()
    {
        var order = await OpenOrderAsync();
        _orders.GetOwnedAsync(Arg.Is<Guid>(id => id != _w.UserId), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Order?)null);
        var stranger = Guid.NewGuid();

        (await _sut.VerifyPaymentAsync(stranger, Verify(order), Ct)).FirstError.ShouldBe(PaymentErrors.OrderNotFound);
        (await _sut.GetOrderAsync(stranger, order.Id, Ct)).FirstError.ShouldBe(PaymentErrors.OrderNotFound);
        (await _sut.CancelOrderAsync(stranger, order.Id, Ct)).FirstError.ShouldBe(PaymentErrors.OrderNotFound);
    }

    // ------------------------------------------------------------------ cancel & simulate

    [Fact]
    public async Task Cancelling_an_unpaid_order_gives_the_coupon_back_but_a_paid_order_cannot_be_cancelled()
    {
        var coupon = _w.Coupon();
        coupon.UsedCount = 1;
        var order = await OpenOrderAsync();
        _orders.GetRedemptionAsync(order.Id, Arg.Any<CancellationToken>())
            .Returns(new CouponRedemption { CouponId = coupon.Id, Coupon = coupon, OrderId = order.Id, Status = CouponRedemptionStatus.Reserved });

        var dto = (await _sut.CancelOrderAsync(_w.UserId, order.Id, Ct)).Value;

        dto.Status.ShouldBe(OrderStatus.Cancelled);
        coupon.UsedCount.ShouldBe(0);

        order.Status = OrderStatus.Paid;
        (await _sut.CancelOrderAsync(_w.UserId, order.Id, Ct)).Value.Status.ShouldBe(OrderStatus.Paid);
    }

    [Fact]
    public async Task Simulation_is_not_available_with_a_real_gateway()
    {
        var order = await OpenOrderAsync();

        var result = await _sut.SimulatePaymentAsync(_w.UserId, new SimulatePaymentRequest(order.Id), Ct);

        result.FirstError.ShouldBe(PaymentErrors.NotAvailable);
    }

    [Fact]
    public async Task With_the_simulated_gateway_a_payment_is_produced_and_the_webhook_can_be_delivered()
    {
        var simulated = Substitute.For<IPaymentGateway, ISimulatedGateway>();
        simulated.Provider.Returns("Simulated");
        simulated.PublicKeyId.Returns("rzp_test_sim");
        simulated.CreateOrderAsync(Arg.Any<GatewayOrderRequest>(), Arg.Any<CancellationToken>()).Returns(new GatewayOrder("order_sim_1", 29900));
        ((ISimulatedGateway)simulated).CreatePayment("order_sim_1", 29900, true).Returns(new SimulatedPaymentDto("order_sim_1", "pay_sim_s_1_29900", "sig"));
        ((ISimulatedGateway)simulated).SignWebhook(Arg.Any<string>()).Returns("webhook-sig");
        _webhooks.HandleAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        var sut = Build(simulated);
        await sut.CreateOrderAsync(_w.UserId, Request(invitation: _w.Invitation.Id), Ct);

        var payment = (await sut.SimulatePaymentAsync(_w.UserId, new SimulatePaymentRequest(_saved!.Id, true, DeliverWebhook: true), Ct)).Value;

        payment.PaymentId.ShouldBe("pay_sim_s_1_29900");
        await _webhooks.Received(1).HandleAsync(
            Arg.Is<string>(body => body.Contains("\"event\":\"payment.captured\"") && body.Contains("\"order_id\":\"order_sim_1\"") && body.Contains("\"amount\":29900")),
            "webhook-sig", Arg.Is<string?>(id => id!.StartsWith("sim_evt_")), Ct);
    }
}
