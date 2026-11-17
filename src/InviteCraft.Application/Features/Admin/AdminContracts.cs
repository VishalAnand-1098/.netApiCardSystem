using InviteCraft.Application.Common.Models;
using InviteCraft.Application.Features.Payments;
using InviteCraft.Domain.Enums;

namespace InviteCraft.Application.Features.Admin;

// ====================================================================== dashboard & reports

/// <summary>Reports are cut on Indian calendar days: "today" starts at 00:00 IST, not UTC.</summary>
public static class ReportTime
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(5.5);

    public static DateOnly DayOf(DateTimeOffset value) => DateOnly.FromDateTime(value.ToOffset(Offset).DateTime);

    /// <summary>The instant a day starts, as UTC (PostgreSQL only accepts offset-0 values as query parameters).</summary>
    public static DateTimeOffset StartOf(DateOnly day) => new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), Offset).ToUniversalTime();
}

public sealed record DashboardKpis(
    decimal RevenueToday, decimal Revenue7Days, decimal Revenue30Days, decimal RevenueTotal,
    int OrdersPaid, decimal AverageOrderValue, decimal Refunded30Days,
    int Customers, int NewCustomers30Days, int Invitations, int UnlockedInvitations, int Downloads30Days,
    int PendingReviews, int AwaitingPaymentOrders);

public sealed record DayPoint(DateOnly Date, int Orders, decimal Revenue);

public sealed record TemplateSalesRow(Guid TemplateId, string Name, int Orders, decimal Revenue);

public sealed record PackageSalesRow(string PackageName, int Orders, decimal Revenue);

public sealed record AdminDashboardDto(
    DashboardKpis Kpis,
    IReadOnlyList<DayPoint> Last30Days,
    IReadOnlyList<TemplateSalesRow> TopTemplates,
    IReadOnlyList<PackageSalesRow> ByPackage,
    IReadOnlyList<AdminOrderListItem> RecentOrders);

/// <summary>?from=2026-11-01&amp;to=2026-11-30&amp;groupBy=day|month (inclusive IST dates, at most 366 days; default last 30 days).</summary>
public sealed record SalesReportQuery
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public string? GroupBy { get; init; }
}

public sealed record SalesRow(string Period, int Orders, decimal Gross, decimal Discounts, decimal Gst, decimal Net, int RefundedOrders, decimal Refunded);

public sealed record SalesReportDto(DateOnly From, DateOnly To, string GroupBy, IReadOnlyList<SalesRow> Rows, SalesRow Totals);

public sealed record CouponUsageRow(string Code, int Redemptions, decimal DiscountGiven);

/// <summary>Order facts the report service groups in memory (kept to a handful of columns).</summary>
public sealed record OrderFact(DateTimeOffset PaidAt, OrderStatus Status, decimal Total, decimal Tax, decimal Discount);

public sealed record PlatformCounts(int Customers, int NewCustomers30Days, int Invitations, int Unlocked, int Downloads30Days, int PendingReviews, int AwaitingPaymentOrders);

// ====================================================================== lists

public sealed record AdminCustomerQuery : PagedRequest
{
    public bool? IsActive { get; init; }
}

public sealed record AdminCustomerListItem(
    Guid Id, string Email, string FullName, string? Phone, bool IsActive, bool EmailVerified, bool IsAdmin,
    DateTimeOffset CreatedAt, DateTimeOffset? LastLoginAt, int Orders, decimal Spent, int Invitations);

public sealed record AdminCustomerDetail(AdminCustomerListItem Customer, IReadOnlyList<AdminOrderListItem> RecentOrders);

public sealed record AdminOrderQuery : PagedRequest
{
    public OrderStatus? Status { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public Guid? UserId { get; init; }
}

public sealed record AdminOrderListItem(
    Guid Id, string OrderNumber, OrderStatus Status, decimal TotalAmount, string? CouponCode, DateTimeOffset CreatedAt, DateTimeOffset? PaidAt,
    Guid UserId, string CustomerName, string CustomerEmail, string? InvitationTitle, string? PackageName);

public sealed record AdminOrderDetailDto(OrderDto Order, Guid UserId, string CustomerName, string CustomerEmail, bool CanRefund);

public sealed record RefundOrderRequest(string? Reason);

public sealed record AdminPaymentQuery : PagedRequest
{
    public PaymentStatus? Status { get; init; }
}

public sealed record AdminPaymentListItem(
    Guid Id, Guid OrderId, string OrderNumber, string CustomerEmail, string? GatewayPaymentId, PaymentStatus Status, string? Method,
    decimal Amount, string Currency, string? ErrorDescription, DateTimeOffset CreatedAt, DateTimeOffset? CapturedAt);

public sealed record AuditLogQuery : PagedRequest
{
    public string? Action { get; init; }
    public string? EntityType { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
}

public sealed record AuditLogItem(Guid Id, DateTimeOffset CreatedAt, string Action, string EntityType, string? EntityId, string? UserEmail, string? NewValues, string? IpAddress);

public sealed record AdminReviewQuery : PagedRequest
{
    public ReviewStatus? Status { get; init; }
}

public sealed record AdminReviewItem(
    Guid Id, string CustomerName, Guid? TemplateId, string? TemplateName, short Rating, string? Title, string? Comment,
    ReviewStatus Status, bool IsFeatured, string? AdminReply, DateTimeOffset CreatedAt);

public sealed record UpdateReviewRequest(ReviewStatus? Status, bool? IsFeatured, string? AdminReply);

// ====================================================================== coupons

public sealed record AdminCouponQuery : PagedRequest
{
    public bool? IsActive { get; init; }
}

public sealed record AdminCouponDto(
    Guid Id, string Code, string? Description, DiscountType DiscountType, decimal DiscountValue, decimal? MaxDiscountAmount, decimal? MinOrderAmount,
    IReadOnlyList<Guid> ApplicablePackageIds, IReadOnlyList<Guid> ApplicableCategoryIds, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt,
    int? UsageLimit, int PerUserLimit, int UsedCount, bool IsActive, DateTimeOffset CreatedAt);

public sealed record SaveCouponRequest(
    string Code, string? Description, DiscountType DiscountType, decimal DiscountValue, decimal? MaxDiscountAmount, decimal? MinOrderAmount,
    IReadOnlyList<Guid>? ApplicablePackageIds, IReadOnlyList<Guid>? ApplicableCategoryIds, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt,
    int? UsageLimit, int PerUserLimit, bool IsActive);

// ====================================================================== repository & services

public interface IAdminRepository
{
    Task<IReadOnlyList<OrderFact>> GetOrderFactsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TemplateSalesRow>> GetTopTemplatesAsync(DateTimeOffset from, DateTimeOffset to, int take, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PackageSalesRow>> GetSalesByPackageAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CouponUsageRow>> GetCouponUsageAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
    Task<decimal> GetPaidRevenueTotalAsync(CancellationToken cancellationToken = default);
    Task<PlatformCounts> GetPlatformCountsAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    Task<PagedResult<AdminCustomerListItem>> ListCustomersAsync(AdminCustomerQuery query, CancellationToken cancellationToken = default);
    Task<AdminCustomerListItem?> GetCustomerAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PagedResult<AdminOrderListItem>> ListOrdersAsync(AdminOrderQuery query, DateTimeOffset? from, DateTimeOffset? toExclusive, CancellationToken cancellationToken = default);
    Task<AdminOrderDetailDto?> GetOrderAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PagedResult<AdminPaymentListItem>> ListPaymentsAsync(AdminPaymentQuery query, CancellationToken cancellationToken = default);
    Task<PagedResult<AuditLogItem>> ListAuditLogsAsync(AuditLogQuery query, DateTimeOffset? from, DateTimeOffset? toExclusive, CancellationToken cancellationToken = default);
    Task<PagedResult<AdminReviewItem>> ListReviewsAsync(AdminReviewQuery query, CancellationToken cancellationToken = default);
    Task<AdminReviewItem?> GetReviewAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PagedResult<AdminCouponDto>> ListCouponsAsync(AdminCouponQuery query, CancellationToken cancellationToken = default);
    Task<int> CountExistingPackagesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
    Task<int> CountExistingCategoriesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
}

public interface IAdminReportService
{
    Task<AdminDashboardDto> GetDashboardAsync(CancellationToken cancellationToken = default);
    Task<Result<SalesReportDto>> GetSalesAsync(SalesReportQuery query, CancellationToken cancellationToken = default);
    Task<Result<string>> GetSalesCsvAsync(SalesReportQuery query, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<TemplateSalesRow>>> GetTopTemplatesAsync(SalesReportQuery query, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<CouponUsageRow>>> GetCouponUsageAsync(SalesReportQuery query, CancellationToken cancellationToken = default);
}

public interface IAdminDirectoryService
{
    Task<PagedResult<AdminCustomerListItem>> ListCustomersAsync(AdminCustomerQuery query, CancellationToken cancellationToken = default);
    Task<Result<AdminCustomerDetail>> GetCustomerAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result<AdminCustomerListItem>> SetCustomerActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default);
    Task<PagedResult<AdminOrderListItem>> ListOrdersAsync(AdminOrderQuery query, CancellationToken cancellationToken = default);
    Task<Result<AdminOrderDetailDto>> GetOrderAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result<AdminOrderDetailDto>> RefundOrderAsync(Guid id, RefundOrderRequest request, CancellationToken cancellationToken = default);
    Task<PagedResult<AdminPaymentListItem>> ListPaymentsAsync(AdminPaymentQuery query, CancellationToken cancellationToken = default);
    Task<PagedResult<AuditLogItem>> ListAuditLogsAsync(AuditLogQuery query, CancellationToken cancellationToken = default);
    Task<PagedResult<AdminReviewItem>> ListReviewsAsync(AdminReviewQuery query, CancellationToken cancellationToken = default);
    Task<Result<AdminReviewItem>> UpdateReviewAsync(Guid id, UpdateReviewRequest request, CancellationToken cancellationToken = default);
}

public interface IAdminCouponService
{
    Task<PagedResult<AdminCouponDto>> ListAsync(AdminCouponQuery query, CancellationToken cancellationToken = default);
    Task<Result<AdminCouponDto>> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result<AdminCouponDto>> CreateAsync(SaveCouponRequest request, CancellationToken cancellationToken = default);
    Task<Result<AdminCouponDto>> UpdateAsync(Guid id, SaveCouponRequest request, CancellationToken cancellationToken = default);
    Task<Result<AdminCouponDto>> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default);
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public static class AdminErrors
{
    public static readonly Error CustomerNotFound = Error.NotFound("Customer.NotFound", "Customer not found.");
    public static readonly Error CouponNotFound = Error.NotFound("Coupon.NotFound", "Coupon not found.");
    public static readonly Error ReviewNotFound = Error.NotFound("Review.NotFound", "Review not found.");
    public static readonly Error CannotDeactivateSelf = Error.Conflict("Customer.CannotDeactivateSelf", "You cannot deactivate your own account.");
    public static readonly Error CannotDeactivateAdmin = Error.Conflict("Customer.CannotDeactivateAdmin", "Administrator accounts cannot be deactivated here.");
    public static readonly Error NotRefundable = Error.Conflict("Order.NotRefundable", "Only paid orders with a captured payment can be refunded.");
    public static readonly Error RefundFailed = Error.Failure("Order.RefundFailed", "The payment provider did not accept the refund. Nothing was changed — try again in a minute or refund from the Razorpay dashboard.");
    public static readonly Error CouponCodeTaken = Error.Conflict("Coupon.CodeTaken", "Another coupon already uses this code.");
    public static Error BadRange(string message) => Error.Validation("Report.InvalidRange", message, "from");
    public static Error Coupon(string field, string message) => Error.Validation("Coupon.Invalid", message, field);
    public static Error Review(string field, string message) => Error.Validation("Review.Invalid", message, field);
    public static Error UsageBelowUsed(int used) => Error.Conflict("Coupon.UsageLimitBelowUsed", $"This coupon has already been used {used} time(s); the limit cannot be lower.");
}
