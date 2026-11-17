using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Application.Common.Models;
using InviteCraft.Application.Features.Payments;
using InviteCraft.Domain.Entities.Commerce;
using InviteCraft.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace InviteCraft.UnitTests.Payments;

public class PaymentWebhookServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly PaymentWorld _w = new();
    private readonly IPaymentGateway _gateway = Substitute.For<IPaymentGateway>();
    private readonly IWebhookEventRepository _events = Substitute.For<IWebhookEventRepository>();
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IPaymentProcessor _processor = Substitute.For<IPaymentProcessor>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IPersistenceConflicts _conflicts = Substitute.For<IPersistenceConflicts>();
    private readonly PaymentWebhookService _sut;
    private readonly Order _order = new() { OrderNumber = "ICO-2026-ABC123", GatewayOrderId = "order_RZP1", TotalAmount = 299m, Status = OrderStatus.AwaitingPayment };

    private PaymentWebhookEvent? _stored;

    public PaymentWebhookServiceTests()
    {
        _sut = new PaymentWebhookService(_gateway, _events, _orders, _processor, _unitOfWork, _conflicts, _w.Clock, NullLogger<PaymentWebhookService>.Instance);
        _gateway.Provider.Returns("Razorpay");
        _gateway.VerifyWebhookSignature(Arg.Any<string>(), "good").Returns(true);
        _events.AddAsync(Arg.Do<PaymentWebhookEvent>(e => _stored = e), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _orders.FindByGatewayOrderIdAsync("order_RZP1", Arg.Any<CancellationToken>()).Returns(_order);
        _processor.ApplyCapturedAsync(Arg.Any<Guid>(), Arg.Any<GatewayPayment>(), Arg.Any<PaymentVerificationSource>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(PaymentOutcome.Applied);
        _processor.RecordFailureAsync(Arg.Any<Guid>(), Arg.Any<GatewayPayment>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        _processor.ApplyRefundAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
    }

    private static string Body(string eventName, string status = "captured", long amount = 29900, string orderId = "order_RZP1", string paymentId = "pay_1") =>
        """
        {"entity":"event","event":"@EVENT@","payload":{"payment":{"entity":{"id":"@PAYMENT@","order_id":"@ORDER@","amount":@AMOUNT@,"currency":"INR","status":"@STATUS@","method":"upi","email":"secret@example.com","contact":"+919999999999"}}}}
        """
        .Replace("@EVENT@", eventName).Replace("@PAYMENT@", paymentId).Replace("@ORDER@", orderId).Replace("@AMOUNT@", amount.ToString()).Replace("@STATUS@", status);

    private Task<Result> Handle(string body, string? signature = "good", string? eventId = "evt_1") => _sut.HandleAsync(body, signature, eventId, Ct);

    // ------------------------------------------------------------------ authentication

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("forged")]
    public async Task A_missing_or_invalid_signature_is_rejected_before_anything_is_stored_or_processed(string? signature)
    {
        var result = await Handle(Body("payment.captured"), signature);

        result.FirstError.ShouldBe(PaymentErrors.WebhookSignature);
        await _events.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await _processor.DidNotReceiveWithAnyArgs().ApplyCapturedAsync(default, default!, default, default, default);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"event":""}""")]
    public async Task A_signed_but_malformed_body_is_a_client_error(string body)
    {
        (await Handle(body)).FirstError.ShouldBe(PaymentErrors.WebhookMalformed);
    }

    // ------------------------------------------------------------------ dispatch

    [Theory]
    [InlineData("payment.captured")]
    [InlineData("order.paid")]
    public async Task A_captured_payment_event_is_applied_to_the_order_through_the_processor(string eventName)
    {
        var result = await Handle(Body(eventName));

        result.IsSuccess.ShouldBeTrue();
        await _processor.Received(1).ApplyCapturedAsync(_order.Id,
            Arg.Is<GatewayPayment>(p => p.Id == "pay_1" && p.AmountPaise == 29900 && p.OrderId == "order_RZP1" && p.Status == "captured"),
            PaymentVerificationSource.Webhook, null, Ct);
        _stored!.Status.ShouldBe(WebhookEventStatus.Processed);
        _stored.EventId.ShouldBe("evt_1");
        _stored.EventType.ShouldBe(eventName);
        _stored.SignatureValid.ShouldBeTrue();
        _stored.ProcessedAt.ShouldBe(PaymentWorld.Now);
    }

    [Fact]
    public async Task Only_non_personal_payment_fields_are_kept_for_support()
    {
        GatewayPayment? seen = null;
        _processor.ApplyCapturedAsync(Arg.Any<Guid>(), Arg.Do<GatewayPayment>(p => seen = p), Arg.Any<PaymentVerificationSource>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(PaymentOutcome.Applied);

        await Handle(Body("payment.captured"));

        seen!.Raw.ShouldContain("\"method\":\"upi\"");
        seen.Raw.ShouldNotContain("secret@example.com");
        seen.Raw.ShouldNotContain("919999999999");
    }

    [Fact]
    public async Task An_authorised_payment_is_captured_first_when_the_account_does_not_auto_capture()
    {
        _gateway.CapturePaymentAsync("pay_1", 29900, "INR", Arg.Any<CancellationToken>())
            .Returns(new GatewayPayment("pay_1", "order_RZP1", "captured", 29900, "INR", "upi", null, null, "{}"));

        await Handle(Body("payment.authorized", status: "authorized"));

        await _gateway.Received(1).CapturePaymentAsync("pay_1", 29900, "INR", Ct);
        await _processor.Received(1).ApplyCapturedAsync(_order.Id, Arg.Is<GatewayPayment>(p => p.Status == "captured"), PaymentVerificationSource.Webhook, null, Ct);
    }

    [Fact]
    public async Task A_payment_that_is_not_captured_is_ignored()
    {
        await Handle(Body("order.paid", status: "created"));

        await _processor.DidNotReceiveWithAnyArgs().ApplyCapturedAsync(default, default!, default, default, default);
        _stored!.Status.ShouldBe(WebhookEventStatus.Ignored);
    }

    [Fact]
    public async Task Events_for_orders_we_do_not_know_are_acknowledged_and_ignored()
    {
        var result = await Handle(Body("payment.captured", orderId: "order_SOMEONE_ELSES"));

        result.IsSuccess.ShouldBeTrue(); // 200, so the gateway does not retry something that can never succeed
        _stored!.Status.ShouldBe(WebhookEventStatus.Ignored);
        await _processor.DidNotReceiveWithAnyArgs().ApplyCapturedAsync(default, default!, default, default, default);
    }

    [Fact]
    public async Task A_failed_payment_event_is_recorded()
    {
        await Handle(Body("payment.failed", status: "failed"));

        await _processor.Received(1).RecordFailureAsync(_order.Id, Arg.Is<GatewayPayment>(p => p.Id == "pay_1"), Ct);
        _stored!.Status.ShouldBe(WebhookEventStatus.Processed);
    }

    [Fact]
    public async Task A_refund_event_is_applied_by_payment_id_and_amount()
    {
        const string refund = """{"event":"refund.processed","payload":{"refund":{"entity":{"id":"rfnd_1","payment_id":"pay_1","amount":29900}}}}""";

        await Handle(refund);

        await _processor.Received(1).ApplyRefundAsync("pay_1", 29900, Ct);
        _stored!.Status.ShouldBe(WebhookEventStatus.Processed);
    }

    [Fact]
    public async Task Event_types_we_do_not_use_are_acknowledged_and_ignored()
    {
        var result = await Handle("""{"event":"settlement.processed","payload":{}}""");

        result.IsSuccess.ShouldBeTrue();
        _stored!.Status.ShouldBe(WebhookEventStatus.Ignored);
    }

    [Fact]
    public async Task An_amount_mismatch_counts_as_handled_because_it_was_flagged_and_a_retry_cannot_fix_it()
    {
        _processor.ApplyCapturedAsync(Arg.Any<Guid>(), Arg.Any<GatewayPayment>(), Arg.Any<PaymentVerificationSource>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<PaymentOutcome>(PaymentErrors.AmountMismatch));

        var result = await Handle(Body("payment.captured", amount: 100));

        result.IsSuccess.ShouldBeTrue();
        _stored!.Status.ShouldBe(WebhookEventStatus.Processed);
        _stored.Error!.ShouldContain("mismatch");
    }

    // ------------------------------------------------------------------ duplicates and retries

    [Theory]
    [InlineData(WebhookEventStatus.Processed)]
    [InlineData(WebhookEventStatus.Ignored)]
    public async Task A_duplicate_of_an_event_that_was_handled_is_a_no_op(WebhookEventStatus status)
    {
        _events.FindAsync("Razorpay", "evt_1", Arg.Any<CancellationToken>()).Returns(new PaymentWebhookEvent { EventId = "evt_1", Status = status });

        var result = await Handle(Body("payment.captured"));

        result.IsSuccess.ShouldBeTrue();
        await _processor.DidNotReceiveWithAnyArgs().ApplyCapturedAsync(default, default!, default, default, default);
        await _events.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task Two_deliveries_of_the_same_event_at_once_are_handled_by_exactly_one_of_them()
    {
        var conflict = new InvalidOperationException("unique (provider, event_id)");
        _conflicts.IsConflict(conflict).Returns(true);
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(conflict);

        var result = await Handle(Body("payment.captured"));

        result.IsSuccess.ShouldBeTrue();
        _conflicts.Received(1).DiscardChanges();
        await _processor.DidNotReceiveWithAnyArgs().ApplyCapturedAsync(default, default!, default, default, default);
    }

    [Fact]
    public async Task A_processing_error_asks_the_gateway_to_retry_and_a_retry_is_processed_again()
    {
        _processor.ApplyCapturedAsync(Arg.Any<Guid>(), Arg.Any<GatewayPayment>(), Arg.Any<PaymentVerificationSource>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<Result<PaymentOutcome>>(new InvalidOperationException("database is down")));
        _events.FindAsync("Razorpay", "evt_1", Arg.Any<CancellationToken>()).Returns((PaymentWebhookEvent?)null);

        var first = await Handle(Body("payment.captured"));

        first.FirstError.ShouldBe(PaymentErrors.WebhookFailed); // non-2xx: Razorpay delivers again
        var record = _stored!;
        record.Status.ShouldBe(WebhookEventStatus.Failed);
        record.Error!.ShouldContain("database is down");
        record.ProcessedAt.ShouldBeNull();

        // The retry finds the Failed record and processes it again.
        _events.FindAsync("Razorpay", "evt_1", Arg.Any<CancellationToken>()).Returns(record);
        _processor.ApplyCapturedAsync(Arg.Any<Guid>(), Arg.Any<GatewayPayment>(), Arg.Any<PaymentVerificationSource>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(PaymentOutcome.Applied);

        var second = await Handle(Body("payment.captured"));

        second.IsSuccess.ShouldBeTrue();
        record.Status.ShouldBe(WebhookEventStatus.Processed);
        record.Attempts.ShouldBeGreaterThanOrEqualTo(2);
        record.Error.ShouldBeNull();
    }

    [Fact]
    public async Task Without_an_event_id_header_the_body_hash_identifies_the_event()
    {
        await Handle(Body("payment.captured"), eventId: null);
        var firstId = _stored!.EventId;

        _stored = null;
        await Handle(Body("payment.captured"), eventId: null);
        var secondId = _stored!.EventId;

        firstId.ShouldStartWith("h_");
        secondId.ShouldBe(firstId);
    }
}

public class OrderExpiryServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly PaymentWorld _w = new();
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IPersistenceConflicts _conflicts = Substitute.For<IPersistenceConflicts>();
    private readonly OrderExpiryService _sut;

    public OrderExpiryServiceTests() =>
        _sut = new OrderExpiryService(_orders, _unitOfWork, _conflicts, Substitute.For<IAuditLogger>(), _w.Clock, NullLogger<OrderExpiryService>.Instance);

    private Order Due(OrderStatus status = OrderStatus.AwaitingPayment)
    {
        var order = new Order { Status = status, OrderNumber = "ICO-2026-X" };
        _orders.GetForProcessingAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        _orders.GetExpiredPayableIdsAsync(PaymentWorld.Now, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([order.Id]);
        return order;
    }

    [Fact]
    public async Task Unpaid_orders_past_their_deadline_expire_and_return_their_coupon()
    {
        var order = Due();
        var coupon = _w.Coupon();
        coupon.UsedCount = 4;
        _orders.GetRedemptionAsync(order.Id, Arg.Any<CancellationToken>())
            .Returns(new CouponRedemption { Coupon = coupon, Status = CouponRedemptionStatus.Reserved });

        (await _sut.ExpireDueAsync(Ct)).ShouldBe(1);

        order.Status.ShouldBe(OrderStatus.Expired);
        coupon.UsedCount.ShouldBe(3);
    }

    [Fact]
    public async Task An_order_with_a_failed_payment_attempt_ends_as_failed_rather_than_expired()
    {
        var order = Due();
        order.Payments.Add(new Payment { Status = PaymentStatus.Failed });

        await _sut.ExpireDueAsync(Ct);

        order.Status.ShouldBe(OrderStatus.Failed);
    }

    [Fact]
    public async Task An_order_that_got_paid_in_the_meantime_is_left_alone()
    {
        var order = Due(OrderStatus.Paid);

        (await _sut.ExpireDueAsync(Ct)).ShouldBe(0);

        order.Status.ShouldBe(OrderStatus.Paid);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Losing_a_race_with_a_payment_skips_that_order()
    {
        Due();
        var conflict = new InvalidOperationException("xmin");
        _conflicts.IsConflict(conflict).Returns(true);
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(conflict);

        (await _sut.ExpireDueAsync(Ct)).ShouldBe(0);

        _conflicts.Received(1).DiscardChanges();
    }
}
