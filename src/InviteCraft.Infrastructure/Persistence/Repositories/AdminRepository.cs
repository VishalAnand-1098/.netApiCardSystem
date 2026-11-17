using InviteCraft.Application.Common.Models;
using InviteCraft.Application.Features.Admin;
using InviteCraft.Application.Features.Payments;
using InviteCraft.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace InviteCraft.Infrastructure.Persistence.Repositories;

/// <summary>Read-side queries for the admin panel: everything is projected straight to DTOs and never tracked.</summary>
internal sealed class AdminRepository(AppDbContext context) : IAdminRepository
{
    private static string Like(string term) => QueryExtensions.ContainsPattern(term);

    // ------------------------------------------------------------------ reports

    public async Task<IReadOnlyList<OrderFact>> GetOrderFactsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) =>
        await context.Orders.AsNoTracking()
            .Where(o => (o.Status == OrderStatus.Paid || o.Status == OrderStatus.Refunded) && o.PaidAt >= from && o.PaidAt < to)
            .Select(o => new OrderFact(o.PaidAt!.Value, o.Status, o.TotalAmount, o.TaxAmount, o.DiscountAmount))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TemplateSalesRow>> GetTopTemplatesAsync(DateTimeOffset from, DateTimeOffset to, int take, CancellationToken cancellationToken = default) =>
        await context.OrderItems.AsNoTracking()
            .Where(i => i.Order.Status == OrderStatus.Paid && i.Order.PaidAt >= from && i.Order.PaidAt < to)
            .GroupBy(i => new { i.TemplateId, i.TemplateName })
            .OrderByDescending(g => g.Sum(i => i.LineTotal)).ThenBy(g => g.Key.TemplateName) // order before projecting: EF cannot order by a DTO constructor argument
            .Select(g => new TemplateSalesRow(g.Key.TemplateId, g.Key.TemplateName, g.Count(), g.Sum(i => i.LineTotal)))
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PackageSalesRow>> GetSalesByPackageAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) =>
        await context.OrderItems.AsNoTracking()
            .Where(i => i.Order.Status == OrderStatus.Paid && i.Order.PaidAt >= from && i.Order.PaidAt < to)
            .GroupBy(i => i.PackageName)
            .OrderByDescending(g => g.Sum(i => i.LineTotal))
            .Select(g => new PackageSalesRow(g.Key, g.Count(), g.Sum(i => i.LineTotal)))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CouponUsageRow>> GetCouponUsageAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) =>
        await context.Orders.AsNoTracking()
            .Where(o => o.Status == OrderStatus.Paid && o.PaidAt >= from && o.PaidAt < to && o.CouponId != null)
            .GroupBy(o => o.Coupon!.Code)
            .OrderByDescending(g => g.Sum(o => o.DiscountAmount))
            .Select(g => new CouponUsageRow(g.Key, g.Count(), g.Sum(o => o.DiscountAmount)))
            .ToListAsync(cancellationToken);

    public async Task<decimal> GetPaidRevenueTotalAsync(CancellationToken cancellationToken = default) =>
        await context.Orders.Where(o => o.Status == OrderStatus.Paid).SumAsync(o => (decimal?)o.TotalAmount, cancellationToken) ?? 0m;

    public async Task<PlatformCounts> GetPlatformCountsAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var since = now.AddDays(-30);
        return new PlatformCounts(
            await context.Users.CountAsync(u => !u.UserRoles.Any(r => r.Role.Name == "Admin"), cancellationToken),
            await context.Users.CountAsync(u => u.CreatedAt >= since && !u.UserRoles.Any(r => r.Role.Name == "Admin"), cancellationToken),
            await context.Invitations.CountAsync(cancellationToken),
            await context.Invitations.CountAsync(i => i.UnlockedPackageId != null, cancellationToken),
            await context.Downloads.CountAsync(d => d.Status == DownloadStatus.Ready && d.CreatedAt >= since, cancellationToken),
            await context.Reviews.CountAsync(r => r.Status == ReviewStatus.Pending, cancellationToken),
            await context.Orders.CountAsync(o => o.Status == OrderStatus.AwaitingPayment && o.ExpiresAt > now, cancellationToken));
    }

    // ------------------------------------------------------------------ customers

    private IQueryable<AdminCustomerListItem> CustomerRows(IQueryable<Domain.Entities.Identity.User> users) =>
        users.Select(u => new AdminCustomerListItem(
            u.Id, u.Email, u.FullName, u.Phone, u.IsActive, u.EmailVerifiedAt != null, u.UserRoles.Any(r => r.Role.Name == "Admin"),
            u.CreatedAt, u.LastLoginAt,
            u.Orders.Count(o => o.Status == OrderStatus.Paid),
            u.Orders.Where(o => o.Status == OrderStatus.Paid).Sum(o => (decimal?)o.TotalAmount) ?? 0m,
            u.Invitations.Count()));

    public Task<PagedResult<AdminCustomerListItem>> ListCustomersAsync(AdminCustomerQuery query, CancellationToken cancellationToken = default)
    {
        var users = context.Users.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = Like(query.Search);
            users = users.Where(u => EF.Functions.ILike(u.Email, pattern, QueryExtensions.LikeEscape) || EF.Functions.ILike(u.FullName, pattern, QueryExtensions.LikeEscape));
        }
        if (query.IsActive is { } active)
            users = users.Where(u => u.IsActive == active);

        users = (query.SortBy?.ToLowerInvariant()) switch
        {
            "name" => users.OrderBy(u => u.FullName, query.SortDir),
            "email" => users.OrderBy(u => u.Email, query.SortDir),
            "lastlogin" => users.OrderBy(u => u.LastLoginAt, query.SortDir),
            _ => users.OrderByDescending(u => u.CreatedAt),
        };
        return CustomerRows(users).ToPagedResultAsync(query, cancellationToken);
    }

    public Task<AdminCustomerListItem?> GetCustomerAsync(Guid id, CancellationToken cancellationToken = default) =>
        CustomerRows(context.Users.AsNoTracking().Where(u => u.Id == id)).FirstOrDefaultAsync(cancellationToken);

    // ------------------------------------------------------------------ orders & payments

    public Task<PagedResult<AdminOrderListItem>> ListOrdersAsync(AdminOrderQuery query, DateTimeOffset? from, DateTimeOffset? toExclusive, CancellationToken cancellationToken = default)
    {
        var orders = context.Orders.AsNoTracking().AsQueryable();
        if (query.Status is { } status)
            orders = orders.Where(o => o.Status == status);
        if (query.UserId is { } userId)
            orders = orders.Where(o => o.UserId == userId);
        if (from is { } f)
            orders = orders.Where(o => o.CreatedAt >= f);
        if (toExclusive is { } t)
            orders = orders.Where(o => o.CreatedAt < t);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = Like(query.Search);
            orders = orders.Where(o => EF.Functions.ILike(o.OrderNumber, pattern, QueryExtensions.LikeEscape)
                || EF.Functions.ILike(o.User.Email, pattern, QueryExtensions.LikeEscape)
                || EF.Functions.ILike(o.User.FullName, pattern, QueryExtensions.LikeEscape));
        }

        return orders.OrderByDescending(o => o.CreatedAt)
            .Select(o => new AdminOrderListItem(o.Id, o.OrderNumber, o.Status, o.TotalAmount, o.Coupon != null ? o.Coupon.Code : null, o.CreatedAt, o.PaidAt,
                o.UserId, o.User.FullName, o.User.Email,
                o.Items.Select(i => i.Invitation.Title).FirstOrDefault(),
                o.Items.Select(i => i.PackageName).FirstOrDefault()))
            .ToPagedResultAsync(query, cancellationToken);
    }

    public async Task<AdminOrderDetailDto?> GetOrderAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var o = await context.Orders.AsNoTracking().AsSplitQuery()
            .Include(x => x.User).Include(x => x.Coupon).Include(x => x.Payments).Include(x => x.Items).ThenInclude(i => i.Invitation)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (o is null)
            return null;

        var dto = new OrderDto(o.Id, o.OrderNumber, o.Status, o.Currency, o.Subtotal, o.DiscountAmount, o.TaxAmount, o.TotalAmount, o.Coupon?.Code,
            o.CreatedAt, o.PaidAt, o.ExpiresAt,
            o.Items.Select(i => new OrderItemDto(i.InvitationId, i.Invitation?.Title ?? string.Empty, i.TemplateName, i.PackageName, i.UnitPrice, i.LineTotal)).ToList(),
            o.Payments.OrderBy(p => p.CreatedAt).Select(p => new OrderPaymentDto(p.GatewayPaymentId, p.Status, p.Method, p.Amount, p.CapturedAt, p.ErrorDescription)).ToList());
        var canRefund = o.Status == OrderStatus.Paid && o.Payments.Any(p => p.Status == PaymentStatus.Captured && p.GatewayPaymentId != null);
        return new AdminOrderDetailDto(dto, o.UserId, o.User.FullName, o.User.Email, canRefund);
    }

    public Task<PagedResult<AdminPaymentListItem>> ListPaymentsAsync(AdminPaymentQuery query, CancellationToken cancellationToken = default)
    {
        var payments = context.Payments.AsNoTracking().AsQueryable();
        if (query.Status is { } status)
            payments = payments.Where(p => p.Status == status);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = Like(query.Search);
            payments = payments.Where(p => EF.Functions.ILike(p.Order.OrderNumber, pattern, QueryExtensions.LikeEscape)
                || EF.Functions.ILike(p.Order.User.Email, pattern, QueryExtensions.LikeEscape)
                || (p.GatewayPaymentId != null && EF.Functions.ILike(p.GatewayPaymentId, pattern, QueryExtensions.LikeEscape)));
        }
        return payments.OrderByDescending(p => p.CreatedAt)
            .Select(p => new AdminPaymentListItem(p.Id, p.OrderId, p.Order.OrderNumber, p.Order.User.Email, p.GatewayPaymentId, p.Status, p.Method,
                p.Amount, p.Currency, p.ErrorDescription, p.CreatedAt, p.CapturedAt))
            .ToPagedResultAsync(query, cancellationToken);
    }

    // ------------------------------------------------------------------ audit, reviews, coupons

    public Task<PagedResult<AuditLogItem>> ListAuditLogsAsync(AuditLogQuery query, DateTimeOffset? from, DateTimeOffset? toExclusive, CancellationToken cancellationToken = default)
    {
        var logs = context.AuditLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(query.Action))
            logs = logs.Where(l => l.Action == query.Action);
        if (!string.IsNullOrWhiteSpace(query.EntityType))
            logs = logs.Where(l => l.EntityType == query.EntityType);
        if (from is { } f)
            logs = logs.Where(l => l.CreatedAt >= f);
        if (toExclusive is { } t)
            logs = logs.Where(l => l.CreatedAt < t);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = Like(query.Search);
            logs = logs.Where(l => EF.Functions.ILike(l.Action, pattern, QueryExtensions.LikeEscape)
                || (l.EntityId != null && EF.Functions.ILike(l.EntityId, pattern, QueryExtensions.LikeEscape))
                || (l.User != null && EF.Functions.ILike(l.User.Email, pattern, QueryExtensions.LikeEscape)));
        }
        return logs.OrderByDescending(l => l.CreatedAt)
            .Select(l => new AuditLogItem(l.Id, l.CreatedAt, l.Action, l.EntityType, l.EntityId, l.User != null ? l.User.Email : null, l.NewValues,
                l.IpAddress != null ? l.IpAddress.ToString() : null))
            .ToPagedResultAsync(query, cancellationToken);
    }

    private static IQueryable<AdminReviewItem> ReviewRows(IQueryable<Domain.Entities.Content.Review> reviews) =>
        reviews.Select(r => new AdminReviewItem(r.Id, r.User.FullName, r.TemplateId, r.Template != null ? r.Template.Name : null, r.Rating, r.Title, r.Comment,
            r.Status, r.IsFeatured, r.AdminReply, r.CreatedAt));

    public Task<PagedResult<AdminReviewItem>> ListReviewsAsync(AdminReviewQuery query, CancellationToken cancellationToken = default)
    {
        var reviews = context.Reviews.AsNoTracking().AsQueryable();
        if (query.Status is { } status)
            reviews = reviews.Where(r => r.Status == status);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = Like(query.Search);
            reviews = reviews.Where(r => (r.Comment != null && EF.Functions.ILike(r.Comment, pattern, QueryExtensions.LikeEscape))
                || EF.Functions.ILike(r.User.FullName, pattern, QueryExtensions.LikeEscape));
        }
        return ReviewRows(reviews.OrderByDescending(r => r.CreatedAt)).ToPagedResultAsync(query, cancellationToken);
    }

    public Task<AdminReviewItem?> GetReviewAsync(Guid id, CancellationToken cancellationToken = default) =>
        ReviewRows(context.Reviews.AsNoTracking().Where(r => r.Id == id)).FirstOrDefaultAsync(cancellationToken);

    public Task<PagedResult<AdminCouponDto>> ListCouponsAsync(AdminCouponQuery query, CancellationToken cancellationToken = default)
    {
        var coupons = context.Coupons.AsNoTracking().AsQueryable();
        if (query.IsActive is { } active)
            coupons = coupons.Where(c => c.IsActive == active);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = Like(query.Search);
            coupons = coupons.Where(c => EF.Functions.ILike(c.Code, pattern, QueryExtensions.LikeEscape)
                || (c.Description != null && EF.Functions.ILike(c.Description, pattern, QueryExtensions.LikeEscape)));
        }
        return coupons.OrderByDescending(c => c.CreatedAt)
            .Select(c => new AdminCouponDto(c.Id, c.Code, c.Description, c.DiscountType, c.DiscountValue, c.MaxDiscountAmount, c.MinOrderAmount,
                c.ApplicablePackageIds, c.ApplicableCategoryIds, c.StartsAt, c.EndsAt, c.UsageLimit, c.PerUserLimit, c.UsedCount, c.IsActive, c.CreatedAt))
            .ToPagedResultAsync(query, cancellationToken);
    }

    public Task<int> CountExistingPackagesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) =>
        context.Packages.CountAsync(p => ids.Contains(p.Id), cancellationToken);

    public Task<int> CountExistingCategoriesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) =>
        context.Categories.CountAsync(c => ids.Contains(c.Id), cancellationToken);
}
