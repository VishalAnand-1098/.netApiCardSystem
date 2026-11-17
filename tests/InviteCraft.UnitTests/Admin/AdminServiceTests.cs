using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Application.Common.Models;
using InviteCraft.Application.Features.Admin;
using InviteCraft.Application.Features.Auth;
using InviteCraft.Application.Features.Payments;
using InviteCraft.Domain.Entities.Commerce;
using InviteCraft.Domain.Entities.Content;
using InviteCraft.Domain.Entities.Identity;
using InviteCraft.Domain.Enums;
using InviteCraft.UnitTests.Payments;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace InviteCraft.UnitTests.Admin;

public class AdminReportServiceTests
{
    [Fact]
    public void Day_boundaries_are_UTC_instants_so_they_can_be_sent_to_PostgreSQL_as_query_parameters()
    {
        var start = ReportTime.StartOf(new DateOnly(2026, 11, 11));

        start.Offset.ShouldBe(TimeSpan.Zero);                                                   // Npgsql rejects any other offset
        start.ShouldBe(new DateTimeOffset(2026, 11, 10, 18, 30, 0, TimeSpan.Zero));            // 00:00 IST
        ReportTime.DayOf(start).ShouldBe(new DateOnly(2026, 11, 11));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    // 2026-11-10 20:00 UTC is already 2026-11-11 01:30 in India
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 20, 0, 0, TimeSpan.Zero);

    private readonly IAdminRepository _repository = Substitute.For<IAdminRepository>();
    private readonly AdminReportService _sut;

    public AdminReportServiceTests()
    {
        _sut = new AdminReportService(_repository, new FixedClock(Now));
        _repository.GetOrderFactsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Ct).Returns(Facts());
        _repository.ListOrdersAsync(Arg.Any<AdminOrderQuery>(), null, null, Ct).Returns(PagedResult<AdminOrderListItem>.Empty(1, 6));
        _repository.GetPlatformCountsAsync(Arg.Any<DateTimeOffset>(), Ct).Returns(new PlatformCounts(10, 3, 20, 5, 7, 2, 1));
        _repository.GetPaidRevenueTotalAsync(Ct).Returns(9999m);
    }

    private static IReadOnlyList<OrderFact> Facts() =>
    [
        new(new DateTimeOffset(2026, 11, 10, 19, 0, 0, TimeSpan.Zero), OrderStatus.Paid, 299m, 45.61m, 0m),      // 00:30 IST on the 11th → "today"
        new(new DateTimeOffset(2026, 11, 10, 10, 0, 0, TimeSpan.Zero), OrderStatus.Paid, 149m, 22.73m, 50m),     // the 10th
        new(new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero), OrderStatus.Paid, 549m, 83.75m, 0m),
        new(new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.Zero), OrderStatus.Refunded, 299m, 45.61m, 0m),
    ];

    [Fact]
    public async Task Dashboard_cuts_days_in_india_time_and_fills_the_30_day_series()
    {
        var dashboard = await _sut.GetDashboardAsync(Ct);

        dashboard.Last30Days.Count.ShouldBe(30);
        dashboard.Last30Days[^1].Date.ShouldBe(new DateOnly(2026, 11, 11));
        dashboard.Last30Days[^1].Revenue.ShouldBe(299m);   // paid at 19:00 UTC belongs to the 11th in IST
        dashboard.Last30Days[^2].Revenue.ShouldBe(149m);
        dashboard.Last30Days[0].Date.ShouldBe(new DateOnly(2026, 10, 13));
        dashboard.Last30Days.Count(d => d.Orders == 0).ShouldBe(27);

        dashboard.Kpis.RevenueToday.ShouldBe(299m);
        dashboard.Kpis.Revenue7Days.ShouldBe(448m);
        dashboard.Kpis.Revenue30Days.ShouldBe(997m);        // paid only: the refunded order is not revenue
        dashboard.Kpis.Refunded30Days.ShouldBe(299m);
        dashboard.Kpis.OrdersPaid.ShouldBe(3);
        dashboard.Kpis.AverageOrderValue.ShouldBe(332.33m);
        dashboard.Kpis.RevenueTotal.ShouldBe(9999m);
        dashboard.Kpis.PendingReviews.ShouldBe(2);
    }

    [Fact]
    public async Task Sales_report_groups_by_day_with_zero_rows_and_totals()
    {
        var result = await _sut.GetSalesAsync(new SalesReportQuery { From = new DateOnly(2026, 11, 1), To = new DateOnly(2026, 11, 3) }, Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Rows.Select(r => r.Period).ShouldBe(["2026-11-01", "2026-11-02", "2026-11-03"]);
        result.Value.Rows[0].Gross.ShouldBe(549m);
        result.Value.Rows[1].Orders.ShouldBe(0);
        result.Value.Rows[1].RefundedOrders.ShouldBe(1);
        result.Value.Totals.Gross.ShouldBe(997m);
        result.Value.Totals.Discounts.ShouldBe(50m);
        result.Value.Totals.Net.ShouldBe(997m - (45.61m + 22.73m + 83.75m));
    }

    [Fact]
    public async Task Sales_report_can_group_by_month()
    {
        var result = await _sut.GetSalesAsync(new SalesReportQuery { From = new DateOnly(2026, 10, 15), To = new DateOnly(2026, 11, 30), GroupBy = "month" }, Ct);

        result.Value.Rows.Select(r => r.Period).ShouldBe(["2026-10", "2026-11"]);
        result.Value.Rows[1].Orders.ShouldBe(3);
    }

    [Theory]
    [InlineData("2026-11-05", "2026-11-01", null)]
    [InlineData("2025-01-01", "2026-11-01", null)]
    [InlineData("2026-11-01", "2026-11-02", "week")]
    public async Task Sales_report_rejects_bad_ranges_and_groupings(string from, string to, string? groupBy)
    {
        var result = await _sut.GetSalesAsync(new SalesReportQuery { From = DateOnly.Parse(from), To = DateOnly.Parse(to), GroupBy = groupBy }, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Errors[0].Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public async Task Sales_csv_has_a_header_one_line_per_period_and_a_total_with_invariant_numbers()
    {
        var result = await _sut.GetSalesCsvAsync(new SalesReportQuery { From = new DateOnly(2026, 11, 1), To = new DateOnly(2026, 11, 2) }, Ct);

        var lines = result.Value.TrimEnd().Split("\r\n");
        lines[0].ShouldStartWith("Period,Orders,Gross");
        lines.Length.ShouldBe(4);
        lines[1].ShouldBe("2026-11-01,1,549.00,0.00,83.75,465.25,0,0.00");
        lines[^1].ShouldStartWith("Total,3,997.00"); // the stubbed repository ignores the window
    }
}

public class AdminDirectoryServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 20, 0, 0, TimeSpan.Zero);

    private readonly IAdminRepository _repository = Substitute.For<IAdminRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IRefreshTokenRepository _tokens = Substitute.For<IRefreshTokenRepository>();
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IRepository<Review> _reviews = Substitute.For<IRepository<Review>>();
    private readonly IPaymentGateway _gateway = Substitute.For<IPaymentGateway>();
    private readonly IPaymentProcessor _processor = Substitute.For<IPaymentProcessor>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IAuditLogger _audit = Substitute.For<IAuditLogger>();
    private readonly AdminDirectoryService _sut;
    private readonly Guid _adminId = Guid.NewGuid();

    public AdminDirectoryServiceTests()
    {
        _currentUser.UserId.Returns(_adminId);
        _sut = new AdminDirectoryService(_repository, _users, _tokens, _orders, _reviews, _gateway, _processor, _unitOfWork, _currentUser, _audit,
            new FixedClock(Now), NullLogger<AdminDirectoryService>.Instance);
    }

    private static Order PaidOrder(out Payment payment)
    {
        var order = new Order { OrderNumber = "ICO-2026-ABC123", Status = OrderStatus.Paid, TotalAmount = 299m };
        payment = new Payment { OrderId = order.Id, GatewayPaymentId = "pay_1", Amount = 299m, Status = PaymentStatus.Captured };
        order.Payments.Add(payment);
        return order;
    }

    private static AdminOrderDetailDto Detail(Guid id) => new(
        new OrderDto(id, "ICO-2026-ABC123", OrderStatus.Refunded, "INR", 299m, 0m, 45.61m, 299m, null, Now, Now, null, [], []), Guid.NewGuid(), "A", "a@x.com", false);

    // ------------------------------------------------------------------ refunds

    [Fact]
    public async Task Refund_asks_the_provider_for_the_full_captured_amount_then_applies_it_locally()
    {
        var order = PaidOrder(out _);
        _orders.GetForProcessingAsync(order.Id, Ct).Returns(order);
        _gateway.RefundPaymentAsync("pay_1", 29900, Arg.Any<string>(), Ct).Returns(new GatewayRefund("rfnd_1", 29900, "processed"));
        _repository.GetOrderAsync(order.Id, Ct).Returns(Detail(order.Id));

        var result = await _sut.RefundOrderAsync(order.Id, new RefundOrderRequest("Duplicate purchase"), Ct);

        result.IsSuccess.ShouldBeTrue(result.FirstError?.Message);
        await _gateway.Received(1).RefundPaymentAsync("pay_1", 29900, "Duplicate purchase", Ct);
        await _processor.Received(1).ApplyRefundAsync("pay_1", 29900, Ct);
        _audit.Received().Log("AdminRefundIssued", Arg.Any<string>(), order.Id.ToString(), _adminId, Arg.Any<object?>());
    }

    [Fact]
    public async Task Refund_changes_nothing_locally_when_the_provider_refuses()
    {
        var order = PaidOrder(out _);
        _orders.GetForProcessingAsync(order.Id, Ct).Returns(order);
        _gateway.RefundPaymentAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<string>(), Ct).Returns<GatewayRefund>(_ => throw new HttpRequestException("boom"));

        var result = await _sut.RefundOrderAsync(order.Id, new RefundOrderRequest(null), Ct);

        result.FirstError.ShouldBe(AdminErrors.RefundFailed);
        await _processor.DidNotReceive().ApplyRefundAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.Refunded)]
    [InlineData(OrderStatus.Failed)]
    public async Task Refund_is_refused_unless_the_order_is_paid(OrderStatus status)
    {
        var order = PaidOrder(out _);
        order.Status = status;
        _orders.GetForProcessingAsync(order.Id, Ct).Returns(order);

        (await _sut.RefundOrderAsync(order.Id, new RefundOrderRequest(null), Ct)).FirstError.ShouldBe(AdminErrors.NotRefundable);
        await _gateway.DidNotReceive().RefundPaymentAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_of_an_unknown_order_is_not_found_and_a_paid_order_without_a_captured_payment_is_not_refundable()
    {
        (await _sut.RefundOrderAsync(Guid.NewGuid(), new RefundOrderRequest(null), Ct)).FirstError.ShouldBe(PaymentErrors.OrderNotFound);

        var order = PaidOrder(out var payment);
        payment.Status = PaymentStatus.Failed;
        _orders.GetForProcessingAsync(order.Id, Ct).Returns(order);
        (await _sut.RefundOrderAsync(order.Id, new RefundOrderRequest(null), Ct)).FirstError.ShouldBe(AdminErrors.NotRefundable);
    }

    // ------------------------------------------------------------------ customers

    private User Customer(bool admin = false)
    {
        var user = new User { Email = "c@example.com", FullName = "C", IsActive = true };
        if (admin) user.UserRoles.Add(new UserRole { UserId = user.Id, Role = new Role { Name = "Admin" } });
        _users.GetByIdWithRolesAsync(user.Id, Ct).Returns(user);
        _repository.GetCustomerAsync(user.Id, Ct).Returns(new AdminCustomerListItem(user.Id, user.Email, user.FullName, null, user.IsActive, true, admin, Now, null, 0, 0m, 0));
        return user;
    }

    [Fact]
    public async Task Deactivating_a_customer_blocks_sign_in_and_revokes_every_session()
    {
        var user = Customer();
        var token = new RefreshToken { UserId = user.Id };
        _tokens.GetActiveByUserAsync(user.Id, Now, Ct).Returns([token]);

        var result = await _sut.SetCustomerActiveAsync(user.Id, false, Ct);

        result.IsSuccess.ShouldBeTrue();
        user.IsActive.ShouldBeFalse();
        token.RevokedAt.ShouldBe(Now);
        token.RevokedReason.ShouldBe("AccountDeactivated");
        await _unitOfWork.Received(1).SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task Reactivating_is_idempotent_and_does_not_save_when_nothing_changes()
    {
        var user = Customer();

        (await _sut.SetCustomerActiveAsync(user.Id, true, Ct)).IsSuccess.ShouldBeTrue();

        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Admins_cannot_deactivate_themselves_or_other_admins_and_unknown_customers_are_not_found()
    {
        (await _sut.SetCustomerActiveAsync(_adminId, false, Ct)).FirstError.ShouldBe(AdminErrors.CannotDeactivateSelf);

        var otherAdmin = Customer(admin: true);
        (await _sut.SetCustomerActiveAsync(otherAdmin.Id, false, Ct)).FirstError.ShouldBe(AdminErrors.CannotDeactivateAdmin);

        (await _sut.SetCustomerActiveAsync(Guid.NewGuid(), false, Ct)).FirstError.ShouldBe(AdminErrors.CustomerNotFound);
    }

    // ------------------------------------------------------------------ reviews

    private Review StoredReview(ReviewStatus status = ReviewStatus.Pending)
    {
        var review = new Review { Status = status, Rating = 5 };
        _reviews.GetByIdAsync(review.Id, Ct).Returns(review);
        _repository.GetReviewAsync(review.Id, Ct).Returns(new AdminReviewItem(review.Id, "A", null, null, 5, null, null, status, false, null, Now));
        return review;
    }

    [Fact]
    public async Task Approving_and_featuring_in_one_go_works_and_replies_are_trimmed()
    {
        var review = StoredReview();

        var result = await _sut.UpdateReviewAsync(review.Id, new UpdateReviewRequest(ReviewStatus.Approved, true, "  Thank you!  "), Ct);

        result.IsSuccess.ShouldBeTrue();
        review.Status.ShouldBe(ReviewStatus.Approved);
        review.IsFeatured.ShouldBeTrue();
        review.AdminReply.ShouldBe("Thank you!");
    }

    [Fact]
    public async Task Only_approved_reviews_can_be_featured_and_rejecting_unfeatures()
    {
        var pending = StoredReview();
        (await _sut.UpdateReviewAsync(pending.Id, new UpdateReviewRequest(null, true, null), Ct)).FirstError!.Field.ShouldBe("isFeatured");

        var featured = StoredReview(ReviewStatus.Approved);
        featured.IsFeatured = true;
        (await _sut.UpdateReviewAsync(featured.Id, new UpdateReviewRequest(ReviewStatus.Rejected, false, null), Ct)).IsSuccess.ShouldBeTrue();
        featured.IsFeatured.ShouldBeFalse();
    }

    [Fact]
    public async Task Rejecting_a_featured_review_without_mentioning_the_flag_succeeds_and_un_features_it()
    {
        var review = StoredReview(ReviewStatus.Approved);
        review.IsFeatured = true;

        var result = await _sut.UpdateReviewAsync(review.Id, new UpdateReviewRequest(ReviewStatus.Rejected, null, null), Ct);

        result.IsSuccess.ShouldBeTrue(result.FirstError?.Message);
        review.Status.ShouldBe(ReviewStatus.Rejected);
        review.IsFeatured.ShouldBeFalse();
    }

    [Fact]
    public async Task Reply_length_is_limited_an_empty_reply_clears_it_and_unknown_reviews_are_not_found()
    {
        var review = StoredReview();
        review.AdminReply = "old";

        (await _sut.UpdateReviewAsync(review.Id, new UpdateReviewRequest(null, null, new string('x', AdminDirectoryService.MaxReplyLength + 1)), Ct))
            .FirstError!.Field.ShouldBe("adminReply");
        (await _sut.UpdateReviewAsync(review.Id, new UpdateReviewRequest(null, null, "   "), Ct)).IsSuccess.ShouldBeTrue();
        review.AdminReply.ShouldBeNull();

        (await _sut.UpdateReviewAsync(Guid.NewGuid(), new UpdateReviewRequest(null, null, null), Ct)).FirstError.ShouldBe(AdminErrors.ReviewNotFound);
    }
}

public class AdminCouponServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IAdminRepository _repository = Substitute.For<IAdminRepository>();
    private readonly ICouponRepository _coupons = Substitute.For<ICouponRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IPersistenceConflicts _conflicts = Substitute.For<IPersistenceConflicts>();
    private readonly AdminCouponService _sut;

    public AdminCouponServiceTests()
    {
        _sut = new AdminCouponService(_repository, _coupons, _unitOfWork, _conflicts, Substitute.For<ICurrentUser>(), Substitute.For<IAuditLogger>());
    }

    private static SaveCouponRequest Valid(string code = " spring10 ") => new(code, "Spring sale", DiscountType.Percentage, 10m, 100m, 100m, null, null, null, null, 50, 1, true);

    [Fact]
    public async Task Create_normalises_the_code_and_stores_the_coupon()
    {
        Coupon? added = null;
        await _coupons.AddAsync(Arg.Do<Coupon>(c => added = c), Ct);

        var result = await _sut.CreateAsync(Valid(), Ct);

        result.IsSuccess.ShouldBeTrue(result.FirstError?.Message);
        added!.Code.ShouldBe("SPRING10");
        added.DiscountValue.ShouldBe(10m);
        result.Value.Code.ShouldBe("SPRING10");
        await _unitOfWork.Received(1).SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task Create_refuses_a_code_that_is_taken_even_when_the_race_is_only_caught_by_the_unique_index()
    {
        _coupons.ExistsAsync(Arg.Any<System.Linq.Expressions.Expression<Func<Coupon, bool>>>(), Ct).Returns(true);
        (await _sut.CreateAsync(Valid(), Ct)).FirstError.ShouldBe(AdminErrors.CouponCodeTaken);

        _coupons.ExistsAsync(Arg.Any<System.Linq.Expressions.Expression<Func<Coupon, bool>>>(), Ct).Returns(false);
        _unitOfWork.SaveChangesAsync(Ct).Returns<int>(_ => throw new InvalidOperationException("unique"));
        _conflicts.IsConflict(Arg.Any<Exception>()).Returns(true);
        (await _sut.CreateAsync(Valid(), Ct)).FirstError.ShouldBe(AdminErrors.CouponCodeTaken);
        _conflicts.Received().DiscardChanges();
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("has space")]
    [InlineData("bad!code")]
    [InlineData("")]
    public async Task Create_rejects_malformed_codes(string code)
    {
        var result = await _sut.CreateAsync(Valid(code), Ct);
        result.Errors.ShouldContain(e => e.Field == "code");
    }

    [Fact]
    public async Task Create_validates_the_discount_dates_and_limits()
    {
        var bad = Valid() with { DiscountValue = 150m, StartsAt = DateTimeOffset.UtcNow, EndsAt = DateTimeOffset.UtcNow.AddDays(-1), UsageLimit = 0, PerUserLimit = 0 };

        var fields = (await _sut.CreateAsync(bad, Ct)).Errors.Select(e => e.Field).ToList();

        fields.ShouldBe(["discountValue", "endsAt", "usageLimit", "perUserLimit"], ignoreOrder: true);
    }

    [Fact]
    public async Task A_fixed_coupon_needs_a_positive_value_and_cannot_have_a_percentage_cap()
    {
        var fixedCoupon = Valid() with { DiscountType = DiscountType.FixedAmount, DiscountValue = 0m, MaxDiscountAmount = 50m };

        var fields = (await _sut.CreateAsync(fixedCoupon, Ct)).Errors.Select(e => e.Field).ToList();

        fields.ShouldBe(["discountValue", "maxDiscountAmount"], ignoreOrder: true);
    }

    [Fact]
    public async Task Unknown_packages_or_categories_are_rejected()
    {
        var packageId = Guid.NewGuid();
        _repository.CountExistingPackagesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Ct).Returns(0);

        var result = await _sut.CreateAsync(Valid() with { ApplicablePackageIds = [packageId] }, Ct);

        result.Errors.ShouldContain(e => e.Field == "applicablePackageIds");
    }

    [Fact]
    public async Task Update_cannot_lower_the_usage_limit_below_what_was_used()
    {
        var coupon = new Coupon { Code = "SPRING10", UsedCount = 7, DiscountValue = 10m };
        _coupons.GetByIdAsync(coupon.Id, Ct).Returns(coupon);

        var result = await _sut.UpdateAsync(coupon.Id, Valid("SPRING10") with { UsageLimit = 5 }, Ct);

        result.FirstError!.Code.ShouldBe("Coupon.UsageLimitBelowUsed");
    }

    [Fact]
    public async Task Delete_is_refused_once_the_coupon_has_been_used_but_works_for_unused_ones()
    {
        var used = new Coupon { Code = "OLD", UsedCount = 1 };
        var unused = new Coupon { Code = "NEW" };
        _coupons.GetByIdAsync(used.Id, Ct).Returns(used);
        _coupons.GetByIdAsync(unused.Id, Ct).Returns(unused);

        (await _sut.DeleteAsync(used.Id, Ct)).FirstError!.Code.ShouldBe("Coupon.InUse");
        (await _sut.DeleteAsync(unused.Id, Ct)).IsSuccess.ShouldBeTrue();
        _coupons.Received(1).Remove(unused);
        _coupons.DidNotReceive().Remove(used);
    }

    [Fact]
    public async Task Toggling_an_unknown_coupon_is_not_found()
    {
        (await _sut.SetActiveAsync(Guid.NewGuid(), false, Ct)).FirstError.ShouldBe(AdminErrors.CouponNotFound);
    }
}
