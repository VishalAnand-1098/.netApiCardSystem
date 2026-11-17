using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Application.Features.Invitations;
using InviteCraft.Application.Features.Notifications;
using InviteCraft.Application.Features.Payments;
using InviteCraft.Application.Features.Templates;
using InviteCraft.Domain.Entities.Commerce;
using InviteCraft.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace InviteCraft.UnitTests.Payments;

public class PaymentProcessorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly PaymentWorld _w = new();
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IInvitationRepository _invitations = Substitute.For<IInvitationRepository>();
    private readonly ITemplateRepository _templates = Substitute.For<ITemplateRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IPersistenceConflicts _conflicts = Substitute.For<IPersistenceConflicts>();
    private readonly IAuditLogger _audit = Substitute.For<IAuditLogger>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly PaymentProcessor _sut;
    private readonly Order _order;

    public PaymentProcessorTests()
    {
        _sut = new PaymentProcessor(_orders, _invitations, _templates, _unitOfWork, _conflicts, _audit, _notifications, _w.Clock, NullLogger<PaymentProcessor>.Instance);

        _order = new Order
        {
            UserId = _w.UserId, OrderNumber = "ICO-2026-ABC123", Status = OrderStatus.AwaitingPayment, GatewayOrderId = "order_RZP1", Currency = "INR",
            Subtotal = 299m, TotalAmount = 299m, TaxAmount = 45.61m,
        };
        _order.Items.Add(new OrderItem { OrderId = _order.Id, InvitationId = _w.Invitation.Id, TemplateId = _w.Template.Id, PackageId = _w.Premium.Id, UnitPrice = 299m, LineTotal = 299m });

        _orders.GetForProcessingAsync(_order.Id, Arg.Any<CancellationToken>()).Returns(_order);
        _invitations.GetByIdAsync(_w.Invitation.Id, Arg.Any<CancellationToken>()).Returns(_w.Invitation);
        _templates.GetActivePackagesAsync(Arg.Any<CancellationToken>()).Returns(_w.Packages);
    }

    private GatewayPayment Payment(string id = "pay_1", long? paise = null, string status = "captured", string orderId = "order_RZP1", string currency = "INR") =>
        new(id, orderId, status, paise ?? 29900, currency, "upi", null, null, """{"id":"pay_1"}""");

    private Task<InviteCraft.Application.Common.Models.Result<PaymentOutcome>> Capture(GatewayPayment? payment = null, PaymentVerificationSource source = PaymentVerificationSource.Checkout) =>
        _sut.ApplyCapturedAsync(_order.Id, payment ?? Payment(), source, "sig", Ct);

    // ------------------------------------------------------------------ the happy path

    [Fact]
    public async Task A_captured_payment_marks_the_order_paid_records_the_payment_and_unlocks_the_invitation()
    {
        var result = await Capture();

        result.Value.ShouldBe(PaymentOutcome.Applied);
        _order.Status.ShouldBe(OrderStatus.Paid);
        _order.PaidAt.ShouldBe(PaymentWorld.Now);

        var payment = _order.Payments.Single();
        (payment.GatewayPaymentId, payment.Status, payment.Amount, payment.Method, payment.VerifiedVia, payment.GatewaySignature)
            .ShouldBe(("pay_1", PaymentStatus.Captured, 299m, "upi", PaymentVerificationSource.Checkout, "sig"));
        payment.GatewayOrderId.ShouldBe("order_RZP1");
        payment.CapturedAt.ShouldBe(PaymentWorld.Now);

        _w.Invitation.UnlockedPackageId.ShouldBe(_w.Premium.Id);
        _w.Invitation.UnlockedAt.ShouldBe(PaymentWorld.Now);
        _w.Invitation.EditableUntil.ShouldBe(PaymentWorld.Now.AddDays(30)); // the package's editing window starts at purchase

        await _unitOfWork.Received(1).SaveChangesAsync(Ct);
        await _templates.Received(1).IncrementSalesCountAsync(_w.Template.Id, Ct);
        _audit.Received().Log("OrderPaid", nameof(Order), _order.Id.ToString(), _w.UserId, Arg.Any<object?>());
    }

    [Fact]
    public async Task A_package_without_an_editing_window_can_be_edited_forever()
    {
        _order.Items.Single().PackageId = _w.Luxury.Id;

        await Capture();

        _w.Invitation.UnlockedPackageId.ShouldBe(_w.Luxury.Id);
        _w.Invitation.EditableUntil.ShouldBeNull();
    }

    [Fact]
    public async Task The_webhook_after_the_checkout_callback_is_a_no_op()
    {
        await Capture(source: PaymentVerificationSource.Checkout);
        _w.Invitation.UnlockedAt = PaymentWorld.Now.AddDays(-1); // sentinel: a second application would overwrite this

        var second = await Capture(source: PaymentVerificationSource.Webhook);

        second.Value.ShouldBe(PaymentOutcome.AlreadyProcessed);
        _order.Payments.Count.ShouldBe(1);
        _w.Invitation.UnlockedAt.ShouldBe(PaymentWorld.Now.AddDays(-1));
        await _unitOfWork.Received(1).SaveChangesAsync(Ct);
        await _templates.Received(1).IncrementSalesCountAsync(Arg.Any<Guid>(), Ct); // counted once
    }

    [Fact]
    public async Task A_reserved_coupon_becomes_redeemed_when_the_order_is_paid()
    {
        var coupon = _w.Coupon();
        _order.CouponId = coupon.Id;
        var redemption = new CouponRedemption { CouponId = coupon.Id, Coupon = coupon, OrderId = _order.Id, Status = CouponRedemptionStatus.Reserved };
        _orders.GetRedemptionAsync(_order.Id, Arg.Any<CancellationToken>()).Returns(redemption);

        await Capture();

        redemption.Status.ShouldBe(CouponRedemptionStatus.Redeemed);
        redemption.RedeemedAt.ShouldBe(PaymentWorld.Now);
    }

    [Fact]
    public async Task A_late_payment_for_an_expired_order_is_still_honoured_because_the_money_was_taken()
    {
        _order.Status = OrderStatus.Expired;

        (await Capture(source: PaymentVerificationSource.Webhook)).Value.ShouldBe(PaymentOutcome.Applied);

        _order.Status.ShouldBe(OrderStatus.Paid);
        _w.Invitation.IsUnlocked.ShouldBeTrue();
    }

    [Fact]
    public async Task A_payment_for_a_lower_package_never_takes_away_a_higher_one()
    {
        _w.Invitation.UnlockedPackageId = _w.Luxury.Id;

        await Capture(); // the order is for Premium

        _w.Invitation.UnlockedPackageId.ShouldBe(_w.Luxury.Id);
        _order.Status.ShouldBe(OrderStatus.Paid);
    }

    [Fact]
    public async Task A_deleted_invitation_does_not_stop_the_order_from_being_recorded_as_paid()
    {
        _invitations.GetByIdAsync(_w.Invitation.Id, Arg.Any<CancellationToken>()).Returns((InviteCraft.Domain.Entities.Invitations.Invitation?)null);

        (await Capture()).Value.ShouldBe(PaymentOutcome.Applied);

        _order.Status.ShouldBe(OrderStatus.Paid);
    }

    // ------------------------------------------------------------------ integrity

    [Theory]
    [InlineData(100L, "INR", "order_RZP1")]          // wrong amount
    [InlineData(29900L, "USD", "order_RZP1")]        // wrong currency
    [InlineData(29900L, "INR", "order_OTHER")]       // payment belongs to another gateway order
    public async Task A_payment_that_does_not_match_the_order_is_flagged_and_nothing_is_unlocked(long paise, string currency, string orderId)
    {
        var result = await Capture(Payment(paise: paise, currency: currency, orderId: orderId));

        result.FirstError.ShouldBe(PaymentErrors.AmountMismatch);
        _order.Status.ShouldBe(OrderStatus.AwaitingPayment);
        _w.Invitation.IsUnlocked.ShouldBeFalse();
        var recorded = _order.Payments.Single();
        recorded.ErrorCode.ShouldBe("AMOUNT_MISMATCH"); // kept as evidence for support
        recorded.ErrorDescription.ShouldNotBeNullOrEmpty();
        _audit.Received().Log("PaymentAmountMismatch", nameof(Order), _order.Id.ToString(), _w.UserId, Arg.Any<object?>());
        await _templates.DidNotReceive().IncrementSalesCountAsync(Arg.Any<Guid>(), Ct);
    }

    [Fact]
    public async Task A_second_payment_for_an_already_paid_order_is_recorded_as_a_duplicate_and_never_applied_twice()
    {
        await Capture(Payment("pay_1"));
        _w.Invitation.UnlockedAt = PaymentWorld.Now.AddDays(-1);

        var duplicate = await Capture(Payment("pay_2"));

        duplicate.Value.ShouldBe(PaymentOutcome.Flagged);
        _order.Payments.Single(p => p.GatewayPaymentId == "pay_2").ErrorCode.ShouldBe("DUPLICATE_PAYMENT");
        _order.Status.ShouldBe(OrderStatus.Paid);
        _w.Invitation.UnlockedAt.ShouldBe(PaymentWorld.Now.AddDays(-1));
        _audit.Received().Log("DuplicatePaymentFlagged", nameof(Order), _order.Id.ToString(), _w.UserId, Arg.Any<object?>());
    }

    [Fact]
    public async Task Losing_the_race_to_the_other_path_re_reads_the_order_and_finds_it_already_done()
    {
        var conflict = new InvalidOperationException("duplicate gateway_payment_id");
        _conflicts.IsConflict(conflict).Returns(true);
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(conflict);

        // The first read returns an unpaid order; after the conflict the "database" shows what the other path committed.
        var committed = new Order { UserId = _w.UserId, Status = OrderStatus.Paid, GatewayOrderId = "order_RZP1", TotalAmount = 299m };
        committed.Payments.Add(new Payment { GatewayPaymentId = "pay_1", Status = PaymentStatus.Captured, GatewayOrderId = "order_RZP1" });
        _orders.GetForProcessingAsync(_order.Id, Arg.Any<CancellationToken>()).Returns(_order, committed);

        var result = await Capture();

        result.Value.ShouldBe(PaymentOutcome.AlreadyProcessed);
        _conflicts.Received(1).DiscardChanges();
        await _templates.DidNotReceive().IncrementSalesCountAsync(Arg.Any<Guid>(), Ct);
    }

    [Fact]
    public async Task Repeated_conflicts_give_up_with_a_retryable_error_instead_of_looping_forever()
    {
        var conflict = new InvalidOperationException("xmin");
        _conflicts.IsConflict(conflict).Returns(true);
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(conflict);
        // After DiscardChanges the real database returns a fresh, unmodified order each time.
        _orders.GetForProcessingAsync(_order.Id, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            var fresh = new Order { UserId = _w.UserId, Status = OrderStatus.AwaitingPayment, GatewayOrderId = "order_RZP1", TotalAmount = 299m, Currency = "INR" };
            fresh.Items.Add(new OrderItem { InvitationId = _w.Invitation.Id, TemplateId = _w.Template.Id, PackageId = _w.Premium.Id });
            return fresh;
        });

        var result = await Capture();

        result.FirstError.ShouldBe(PaymentErrors.WebhookFailed);
        _conflicts.Received(3).DiscardChanges();
    }

    [Fact]
    public async Task An_unknown_order_is_reported_as_not_found()
    {
        var result = await _sut.ApplyCapturedAsync(Guid.NewGuid(), Payment(), PaymentVerificationSource.Webhook, null, Ct);

        result.FirstError.ShouldBe(PaymentErrors.OrderNotFound);
    }

    // ------------------------------------------------------------------ failures

    [Fact]
    public async Task A_failed_payment_is_recorded_without_closing_the_order_so_the_customer_can_retry()
    {
        var failed = Payment("pay_f", status: "failed") with { ErrorCode = "BAD_REQUEST_ERROR", ErrorDescription = "Card declined" };

        var result = await _sut.RecordFailureAsync(_order.Id, failed, Ct);

        result.IsSuccess.ShouldBeTrue();
        var payment = _order.Payments.Single();
        (payment.Status, payment.ErrorCode, payment.ErrorDescription, payment.CapturedAt).ShouldBe((PaymentStatus.Failed, "BAD_REQUEST_ERROR", "Card declined", null));
        _order.Status.ShouldBe(OrderStatus.AwaitingPayment);
        _w.Invitation.IsUnlocked.ShouldBeFalse();
    }

    [Fact]
    public async Task A_late_failure_event_never_downgrades_a_payment_that_succeeded()
    {
        await Capture(Payment("pay_1"));

        await _sut.RecordFailureAsync(_order.Id, Payment("pay_1", status: "failed"), Ct);

        _order.Payments.Single().Status.ShouldBe(PaymentStatus.Captured);
        _order.Status.ShouldBe(OrderStatus.Paid);
    }

    // ------------------------------------------------------------------ refunds

    private async Task PayAsync()
    {
        await Capture();
        var payment = _order.Payments.Single();
        _orders.FindPaymentAsync("pay_1", Arg.Any<CancellationToken>()).Returns(payment);
        _orders.GetForProcessingAsync(payment.OrderId, Arg.Any<CancellationToken>()).Returns(_order);
        payment.OrderId = _order.Id;
        _orders.GetForProcessingAsync(_order.Id, Arg.Any<CancellationToken>()).Returns(_order);
    }

    [Fact]
    public async Task A_full_refund_marks_the_order_refunded_and_takes_the_entitlements_away()
    {
        await PayAsync();

        var result = await _sut.ApplyRefundAsync("pay_1", 29900, Ct);

        result.IsSuccess.ShouldBeTrue();
        _order.Status.ShouldBe(OrderStatus.Refunded);
        _order.Payments.Single().Status.ShouldBe(PaymentStatus.Refunded);
        _w.Invitation.IsUnlocked.ShouldBeFalse();
        _w.Invitation.EditableUntil.ShouldBeNull();
        _audit.Received().Log("OrderRefunded", nameof(Order), _order.Id.ToString(), _w.UserId, Arg.Any<object?>());
    }

    [Fact]
    public async Task A_partial_refund_is_recorded_but_changes_nothing()
    {
        await PayAsync();

        await _sut.ApplyRefundAsync("pay_1", 5000, Ct);

        _order.Status.ShouldBe(OrderStatus.Paid);
        _w.Invitation.IsUnlocked.ShouldBeTrue();
        _audit.Received().Log("OrderPartiallyRefunded", nameof(Order), _order.Id.ToString(), _w.UserId, Arg.Any<object?>());
    }

    [Fact]
    public async Task A_refund_does_not_lock_an_invitation_that_has_since_been_upgraded()
    {
        await PayAsync();
        _w.Invitation.UnlockedPackageId = _w.Luxury.Id; // upgraded after this order

        await _sut.ApplyRefundAsync("pay_1", 29900, Ct);

        _w.Invitation.UnlockedPackageId.ShouldBe(_w.Luxury.Id);
    }

    [Fact]
    public async Task Refund_events_for_unknown_payments_and_repeated_refunds_are_ignored()
    {
        (await _sut.ApplyRefundAsync("pay_unknown", 100, Ct)).IsSuccess.ShouldBeTrue();

        await PayAsync();
        await _sut.ApplyRefundAsync("pay_1", 29900, Ct);
        _unitOfWork.ClearReceivedCalls();
        (await _sut.ApplyRefundAsync("pay_1", 29900, Ct)).IsSuccess.ShouldBeTrue();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ------------------------------------------------------------------ free orders

    [Fact]
    public async Task A_free_order_is_completed_and_unlocks_without_a_payment()
    {
        var free = new Order { UserId = _w.UserId, OrderNumber = "ICO-2026-FREE01", Status = OrderStatus.Pending, TotalAmount = 0m };
        free.Items.Add(new OrderItem { InvitationId = _w.Invitation.Id, TemplateId = _w.Template.Id, PackageId = _w.Basic.Id });

        await _sut.CompleteFreeOrderAsync(free, Ct);

        free.Status.ShouldBe(OrderStatus.Paid);
        free.PaidAt.ShouldBe(PaymentWorld.Now);
        free.Payments.ShouldBeEmpty();
        _w.Invitation.UnlockedPackageId.ShouldBe(_w.Basic.Id);
        _w.Invitation.EditableUntil.ShouldBe(PaymentWorld.Now.AddDays(7));
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>()); // the caller saves, in the same unit of work as the order
    }

    // ------------------------------------------------------------------ notifications (Phase 12)

    [Fact]
    public async Task The_customer_is_notified_exactly_once_however_often_the_capture_is_reported()
    {
        await Capture();
        await Capture(source: PaymentVerificationSource.Webhook); // the race's loser observes "already processed"

        _notifications.Received(1).OrderPaid(_order);
        _notifications.DidNotReceive().OrderRefunded(Arg.Any<Order>());
    }

    [Fact]
    public async Task A_flagged_payment_does_not_congratulate_the_customer()
    {
        await Capture(Payment(paise: 11));

        _notifications.DidNotReceive().OrderPaid(Arg.Any<Order>());
    }

    [Fact]
    public async Task A_free_order_sends_the_receipt_too()
    {
        var free = new Order { UserId = _w.UserId, OrderNumber = "ICO-2026-FREE01", TotalAmount = 0m };
        free.Items.Add(new OrderItem { OrderId = free.Id, InvitationId = _w.Invitation.Id, TemplateId = _w.Template.Id, PackageId = _w.Basic.Id });

        await _sut.CompleteFreeOrderAsync(free, Ct);

        _notifications.Received(1).OrderPaid(free);
    }

    [Fact]
    public async Task A_full_refund_notifies_the_customer_a_partial_one_does_not()
    {
        await PayAsync();
        await _sut.ApplyRefundAsync("pay_1", 5000, Ct);
        _notifications.DidNotReceive().OrderRefunded(Arg.Any<Order>());

        await _sut.ApplyRefundAsync("pay_1", 29900, Ct);
        _notifications.Received(1).OrderRefunded(_order);
    }
}
