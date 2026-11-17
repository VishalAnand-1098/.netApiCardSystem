using System.Text.Json;
using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Application.Features.Auth;
using InviteCraft.Application.Features.Notifications;
using InviteCraft.Application.Features.Payments;
using InviteCraft.Application.Features.Sharing;
using InviteCraft.Domain.Entities.Commerce;
using InviteCraft.Domain.Entities.Identity;
using InviteCraft.Domain.Entities.Invitations;
using InviteCraft.Domain.Entities.Platform;
using InviteCraft.Domain.Enums;
using InviteCraft.UnitTests.Payments;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace InviteCraft.UnitTests.Notifications;

internal static class NotificationWorld
{
    public static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    public static Order Order(Guid userId)
    {
        var order = new Order
        {
            UserId = userId, OrderNumber = "ICO-2026-ABC123", Status = OrderStatus.Paid, Currency = "INR",
            Subtotal = 299m, DiscountAmount = 29.90m, TaxAmount = 41.05m, TotalAmount = 269.10m, PaidAt = Now,
            Coupon = new Coupon { Code = "WELCOME10" },
        };
        order.Items.Add(new OrderItem
        {
            OrderId = order.Id, TemplateName = "Royal Maroon Wedding", PackageName = "Premium",
            UnitPrice = 299m, LineTotal = 269.10m, Invitation = new Invitation { Title = "Ananya & Rohan" },
        });
        return order;
    }
}

public class NotificationServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly INotificationsRepository _repository = Substitute.For<INotificationsRepository>();
    private readonly List<Notification> _added = [];
    private readonly NotificationService _sut;
    private readonly Guid _userId = Guid.NewGuid();

    public NotificationServiceTests()
    {
        _repository.When(r => r.Add(Arg.Any<Notification>())).Do(ci => _added.Add(ci.Arg<Notification>()));
        _sut = new NotificationService(_repository, new FixedClock(NotificationWorld.Now));
    }

    [Fact]
    public void A_paid_order_queues_one_in_app_note_and_one_pending_email()
    {
        var order = NotificationWorld.Order(_userId);

        _sut.OrderPaid(order);

        _added.Count.ShouldBe(2);
        var inApp = _added.Single(n => n.Channel == NotificationChannel.InApp);
        inApp.UserId.ShouldBe(_userId);
        inApp.Type.ShouldBe(NotificationTypes.OrderPaid);
        inApp.Status.ShouldBe(NotificationStatus.Sent);
        inApp.SentAt.ShouldBe(NotificationWorld.Now);
        inApp.ReadAt.ShouldBeNull();
        inApp.Title.ShouldContain("ICO-2026-ABC123");
        inApp.Body.ShouldContain("269.10");

        var mail = _added.Single(n => n.Channel == NotificationChannel.Email);
        mail.Status.ShouldBe(NotificationStatus.Pending);
        using var data = JsonDocument.Parse(mail.Data!);
        data.RootElement.GetProperty("orderId").GetGuid().ShouldBe(order.Id);
    }

    [Fact]
    public void A_refund_queues_the_refund_pair()
    {
        _sut.OrderRefunded(NotificationWorld.Order(_userId));

        _added.Select(n => n.Type).ShouldAllBe(t => t == NotificationTypes.OrderRefunded);
        _added.Single(n => n.Channel == NotificationChannel.InApp).Body.ShouldContain("locked again");
    }

    [Fact]
    public async Task Marking_everything_read_passes_null_ids_and_a_specific_set_passes_them_through()
    {
        await _sut.MarkReadAsync(_userId, new MarkReadRequest(null), Ct);
        await _repository.Received(1).MarkReadAsync(_userId, null, NotificationWorld.Now, Ct);

        var ids = new[] { Guid.NewGuid() };
        await _sut.MarkReadAsync(_userId, new MarkReadRequest(ids), Ct);
        await _repository.Received(1).MarkReadAsync(_userId, Arg.Is<IReadOnlyList<Guid>>(l => l.SequenceEqual(ids)), NotificationWorld.Now, Ct);
    }
}

public class NotificationDispatcherTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly INotificationsRepository _repository = Substitute.For<INotificationsRepository>();
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IEmailSender _email = Substitute.For<IEmailSender>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly NotificationDispatcher _sut;

    private readonly User _user = new() { Email = "ananya@example.com", FullName = "Ananya Sharma" };
    private readonly Order _order;
    private readonly Notification _pending;

    public NotificationDispatcherTests()
    {
        _order = NotificationWorld.Order(_user.Id);
        _users.GetByIdAsync(_user.Id, Arg.Any<CancellationToken>()).Returns(_user);
        _orders.GetFullAsync(_order.Id, Arg.Any<CancellationToken>()).Returns(_order);

        _pending = new Notification
        {
            UserId = _user.Id, Channel = NotificationChannel.Email, Type = NotificationTypes.OrderPaid,
            Title = "Your receipt · ICO-2026-ABC123 · InviteCraft", Body = "x",
            Data = JsonSerializer.Serialize(new { orderId = _order.Id, orderNumber = _order.OrderNumber }),
        };
        _repository.GetPendingEmailAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([_pending]);

        _sut = new NotificationDispatcher(_repository, _orders, _users, _email, _unitOfWork, new FixedClock(NotificationWorld.Now),
            Options.Create(new NotificationSettings { MaxAttempts = 3 }),
            Options.Create(new SharingSettings { PublicOrigin = "https://cards.example" }),
            NullLogger<NotificationDispatcher>.Instance);
    }

    [Fact]
    public async Task A_receipt_is_composed_from_the_live_order_sent_and_marked_sent()
    {
        EmailMessage? sent = null;
        await _email.SendAsync(Arg.Do<EmailMessage>(m => sent = m), Arg.Any<CancellationToken>());

        (await _sut.DispatchDueAsync(Ct)).ShouldBe(1);

        sent.ShouldNotBeNull();
        sent.ToAddress.ShouldBe("ananya@example.com");
        sent.Subject.ShouldContain("ICO-2026-ABC123");
        sent.HtmlBody.ShouldContain("269.10");          // total
        sent.HtmlBody.ShouldContain("41.05");           // GST inside
        sent.HtmlBody.ShouldContain("WELCOME10");       // coupon line
        sent.HtmlBody.ShouldContain("Ananya &amp; Rohan");
        sent.HtmlBody.ShouldContain("https://cards.example/account/orders/" + _order.Id);
        sent.TextBody.ShouldContain("269.10");
        _pending.Status.ShouldBe(NotificationStatus.Sent);
        _pending.SentAt.ShouldBe(NotificationWorld.Now);
        await _unitOfWork.Received(1).SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task A_refund_email_tells_the_customer_the_money_is_on_its_way_back()
    {
        _pending.Type = NotificationTypes.OrderRefunded;
        EmailMessage? sent = null;
        await _email.SendAsync(Arg.Do<EmailMessage>(m => sent = m), Arg.Any<CancellationToken>());

        await _sut.DispatchDueAsync(Ct);

        sent!.Subject.ShouldContain("refund");
        sent.HtmlBody.ShouldContain("refunded");
        sent.HtmlBody.ShouldContain("269.10");
    }

    [Fact]
    public async Task A_send_problem_is_retried_and_gives_up_after_the_last_attempt()
    {
        _email.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>()).Returns<Task>(_ => throw new InvalidOperationException("smtp down"));

        (await _sut.DispatchDueAsync(Ct)).ShouldBe(0);
        _pending.Attempts.ShouldBe(1);
        _pending.Status.ShouldBe(NotificationStatus.Pending);
        _pending.Error.ShouldBe("smtp down");

        await _sut.DispatchDueAsync(Ct);
        await _sut.DispatchDueAsync(Ct);
        _pending.Attempts.ShouldBe(3);
        _pending.Status.ShouldBe(NotificationStatus.Failed);
    }

    [Fact]
    public async Task A_notification_whose_order_disappeared_fails_without_an_email()
    {
        _orders.GetFullAsync(_order.Id, Arg.Any<CancellationToken>()).Returns((Order?)null);

        (await _sut.DispatchDueAsync(Ct)).ShouldBe(0);

        _pending.Status.ShouldBe(NotificationStatus.Failed);
        await _email.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_unknown_type_falls_back_to_a_plain_email_instead_of_poisoning_the_queue()
    {
        _pending.Type = "something.new";
        _pending.Body = "Plain body";

        (await _sut.DispatchDueAsync(Ct)).ShouldBe(1);

        await _email.Received(1).SendAsync(Arg.Is<EmailMessage>(m => m.TextBody == "Plain body"), Arg.Any<CancellationToken>());
    }
}
