using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Application.Common.Models;
using InviteCraft.Application.Features.Auth;
using InviteCraft.Application.Features.Payments;
using InviteCraft.Domain.Entities.Content;
using InviteCraft.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace InviteCraft.Application.Features.Admin;

/// <summary>Admin views of customers, orders, payments, reviews and the audit log, plus the few actions on them (deactivate, refund, moderate).</summary>
internal sealed class AdminDirectoryService(
    IAdminRepository repository,
    IUserRepository users,
    IRefreshTokenRepository refreshTokens,
    IOrderRepository orders,
    IRepository<Review> reviews,
    IPaymentGateway gateway,
    IPaymentProcessor processor,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IAuditLogger audit,
    TimeProvider timeProvider,
    ILogger<AdminDirectoryService> logger) : IAdminDirectoryService
{
    public const int MaxReplyLength = 1000;

    private static (DateTimeOffset? From, DateTimeOffset? ToExclusive) Window(DateOnly? from, DateOnly? to) =>
        (from is { } f ? ReportTime.StartOf(f) : null, to is { } t ? ReportTime.StartOf(t.AddDays(1)) : null);

    // ------------------------------------------------------------------ customers

    public Task<PagedResult<AdminCustomerListItem>> ListCustomersAsync(AdminCustomerQuery query, CancellationToken cancellationToken = default) =>
        repository.ListCustomersAsync(query, cancellationToken);

    public async Task<Result<AdminCustomerDetail>> GetCustomerAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var customer = await repository.GetCustomerAsync(id, cancellationToken);
        if (customer is null)
            return AdminErrors.CustomerNotFound;

        var recent = await repository.ListOrdersAsync(new AdminOrderQuery { UserId = id, PageSize = 10 }, null, null, cancellationToken);
        return new AdminCustomerDetail(customer, recent.Items);
    }

    public async Task<Result<AdminCustomerListItem>> SetCustomerActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default)
    {
        if (!isActive && id == currentUser.UserId)
            return AdminErrors.CannotDeactivateSelf;

        var user = await users.GetByIdWithRolesAsync(id, cancellationToken);
        if (user is null)
            return AdminErrors.CustomerNotFound;
        if (!isActive && user.UserRoles.Any(r => r.Role?.Name == "Admin"))
            return AdminErrors.CannotDeactivateAdmin;

        if (user.IsActive != isActive)
        {
            user.IsActive = isActive;
            if (!isActive)
            {
                // sign them out everywhere right away
                var now = timeProvider.GetUtcNow();
                foreach (var token in await refreshTokens.GetActiveByUserAsync(id, now, cancellationToken))
                {
                    token.RevokedAt = now;
                    token.RevokedReason = "AccountDeactivated";
                }
            }
            audit.Log(isActive ? "CustomerActivated" : "CustomerDeactivated", "User", id.ToString(), currentUser.UserId, new { user.Email });
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return (await repository.GetCustomerAsync(id, cancellationToken))!;
    }

    // ------------------------------------------------------------------ orders & payments

    public Task<PagedResult<AdminOrderListItem>> ListOrdersAsync(AdminOrderQuery query, CancellationToken cancellationToken = default)
    {
        var (from, to) = Window(query.From, query.To);
        return repository.ListOrdersAsync(query, from, to, cancellationToken);
    }

    public async Task<Result<AdminOrderDetailDto>> GetOrderAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var order = await repository.GetOrderAsync(id, cancellationToken);
        return order is null ? PaymentErrors.OrderNotFound : order;
    }

    /// <summary>
    /// Full refund of a paid order: the provider is asked first and nothing local changes unless it agrees; then the order becomes
    /// Refunded and the invitations lock again (the same path the provider's refund webhook takes, so the later webhook is a no-op).
    /// </summary>
    public async Task<Result<AdminOrderDetailDto>> RefundOrderAsync(Guid id, RefundOrderRequest request, CancellationToken cancellationToken = default)
    {
        var order = await orders.GetForProcessingAsync(id, cancellationToken);
        if (order is null)
            return PaymentErrors.OrderNotFound;

        var payment = order.Payments.FirstOrDefault(p => p.Status == PaymentStatus.Captured && p.GatewayPaymentId is not null);
        if (order.Status != OrderStatus.Paid || payment is null)
            return AdminErrors.NotRefundable;

        var reason = string.IsNullOrWhiteSpace(request.Reason) ? "Refund issued by an administrator" : request.Reason.Trim();
        var paise = PricingRules.ToPaise(payment.Amount);
        GatewayRefund refund;
        try
        {
            refund = await gateway.RefundPaymentAsync(payment.GatewayPaymentId!, paise, reason, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(ex, "Refund of order {OrderNumber} was not accepted by the provider", order.OrderNumber);
            return AdminErrors.RefundFailed;
        }

        audit.Log("AdminRefundIssued", nameof(Domain.Entities.Commerce.Order), order.Id.ToString(), currentUser.UserId,
            new { order.OrderNumber, refundId = refund.Id, amountPaise = refund.AmountPaise, reason });
        await processor.ApplyRefundAsync(payment.GatewayPaymentId!, paise, cancellationToken);

        return (await repository.GetOrderAsync(id, cancellationToken))!;
    }

    public Task<PagedResult<AdminPaymentListItem>> ListPaymentsAsync(AdminPaymentQuery query, CancellationToken cancellationToken = default) =>
        repository.ListPaymentsAsync(query, cancellationToken);

    public Task<PagedResult<AuditLogItem>> ListAuditLogsAsync(AuditLogQuery query, CancellationToken cancellationToken = default)
    {
        var (from, to) = Window(query.From, query.To);
        return repository.ListAuditLogsAsync(query, from, to, cancellationToken);
    }

    // ------------------------------------------------------------------ reviews

    public Task<PagedResult<AdminReviewItem>> ListReviewsAsync(AdminReviewQuery query, CancellationToken cancellationToken = default) =>
        repository.ListReviewsAsync(query, cancellationToken);

    public async Task<Result<AdminReviewItem>> UpdateReviewAsync(Guid id, UpdateReviewRequest request, CancellationToken cancellationToken = default)
    {
        var review = await reviews.GetByIdAsync(id, cancellationToken);
        if (review is null)
            return AdminErrors.ReviewNotFound;

        var status = request.Status ?? review.Status;
        // Asking to feature a review that is not approved is an error; merely rejecting a featured review simply un-features it.
        if (request.IsFeatured == true && status != ReviewStatus.Approved)
            return AdminErrors.Review("isFeatured", "Only approved reviews can be featured.");
        var featured = request.IsFeatured ?? review.IsFeatured;

        string? reply = review.AdminReply;
        if (request.AdminReply is not null)
        {
            reply = request.AdminReply.Trim();
            if (reply.Length > MaxReplyLength)
                return AdminErrors.Review("adminReply", $"A reply can be at most {MaxReplyLength} characters.");
            if (reply.Length == 0)
                reply = null;
        }

        review.Status = status;
        review.IsFeatured = status == ReviewStatus.Approved && featured; // rejecting a review also un-features it
        review.AdminReply = reply;
        audit.Log("ReviewModerated", nameof(Review), id.ToString(), currentUser.UserId, new { status, review.IsFeatured });
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return (await repository.GetReviewAsync(id, cancellationToken))!;
    }
}
