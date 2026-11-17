using InviteCraft.Application.Features.Payments;
using InviteCraft.Domain.Entities.Commerce;
using InviteCraft.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace InviteCraft.Infrastructure.Persistence.Repositories;

internal sealed class OrderRepository(AppDbContext context) : Repository<Order>(context), IOrderRepository
{
    private static readonly OrderStatus[] Payable = [OrderStatus.Pending, OrderStatus.AwaitingPayment];

    /// <summary>The whole order as the customer sees it.</summary>
    private IQueryable<Order> Full => Set
        .Include(o => o.Items).ThenInclude(i => i.Invitation)
        .Include(o => o.Payments)
        .Include(o => o.Coupon);

    /// <summary>What payment processing needs (invitations are loaded by id when they are unlocked).</summary>
    private IQueryable<Order> ForProcessing => Set.Include(o => o.Items).Include(o => o.Payments).Include(o => o.Coupon);

    public Task<Order?> GetOwnedAsync(Guid userId, Guid id, CancellationToken cancellationToken = default) =>
        Full.FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId, cancellationToken);

    public Task<Order?> GetForProcessingAsync(Guid id, CancellationToken cancellationToken = default) =>
        ForProcessing.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    public Task<Order?> GetFullAsync(Guid id, CancellationToken cancellationToken = default) =>
        Full.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    public Task<Order?> FindByGatewayOrderIdAsync(string gatewayOrderId, CancellationToken cancellationToken = default) =>
        ForProcessing.FirstOrDefaultAsync(o => o.GatewayOrderId == gatewayOrderId, cancellationToken);

    public Task<Order?> FindByIdempotencyKeyAsync(Guid userId, string key, CancellationToken cancellationToken = default) =>
        Full.FirstOrDefaultAsync(o => o.UserId == userId && o.IdempotencyKey == key, cancellationToken);

    public Task<Payment?> FindPaymentAsync(string gatewayPaymentId, CancellationToken cancellationToken = default) =>
        Context.Payments.FirstOrDefaultAsync(p => p.GatewayPaymentId == gatewayPaymentId, cancellationToken);

    public Task<int> CountPayableAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        Set.CountAsync(o => o.UserId == userId && Payable.Contains(o.Status) && (o.ExpiresAt == null || o.ExpiresAt > now), cancellationToken);

    public Task<Order?> FindPayableAsync(Guid userId, Guid invitationId, Guid packageId, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        Full.Where(o => o.UserId == userId && Payable.Contains(o.Status) && (o.ExpiresAt == null || o.ExpiresAt > now) &&
                        o.Items.Any(i => i.InvitationId == invitationId && i.PackageId == packageId))
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<decimal> GetPaidAmountAsync(Guid invitationId, CancellationToken cancellationToken = default) =>
        await Context.OrderItems
            .Where(i => i.InvitationId == invitationId && i.Order.Status == OrderStatus.Paid)
            .OrderByDescending(i => i.Order.PaidAt)
            .Select(i => (decimal?)i.LineTotal)
            .FirstOrDefaultAsync(cancellationToken) ?? 0m;

    public async Task<IReadOnlyList<Guid>> GetExpiredPayableIdsAsync(DateTimeOffset now, int take, CancellationToken cancellationToken = default) =>
        await Set.AsNoTracking()
            .Where(o => Payable.Contains(o.Status) && o.ExpiresAt != null && o.ExpiresAt < now)
            .OrderBy(o => o.ExpiresAt)
            .Select(o => o.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

    public Task<CouponRedemption?> GetRedemptionAsync(Guid orderId, CancellationToken cancellationToken = default) =>
        Context.CouponRedemptions.Include(r => r.Coupon).FirstOrDefaultAsync(r => r.OrderId == orderId, cancellationToken);

    public async Task AddRedemptionAsync(CouponRedemption redemption, CancellationToken cancellationToken = default) =>
        await Context.CouponRedemptions.AddAsync(redemption, cancellationToken);
}

internal sealed class CouponRepository(AppDbContext context) : Repository<Coupon>(context), ICouponRepository
{
    public Task<Coupon?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        Set.FirstOrDefaultAsync(c => c.Code == code && c.IsActive, cancellationToken); // the column is citext: case-insensitive

    public Task<int> CountUserRedemptionsAsync(Guid couponId, Guid userId, CancellationToken cancellationToken = default) =>
        Context.CouponRedemptions.CountAsync(r => r.CouponId == couponId && r.UserId == userId && r.Status != CouponRedemptionStatus.Released, cancellationToken);
}

internal sealed class WebhookEventRepository(AppDbContext context) : IWebhookEventRepository
{
    public Task<PaymentWebhookEvent?> FindAsync(string provider, string eventId, CancellationToken cancellationToken = default) =>
        context.PaymentWebhookEvents.FirstOrDefaultAsync(e => e.Provider == provider && e.EventId == eventId, cancellationToken);

    public async Task AddAsync(PaymentWebhookEvent webhookEvent, CancellationToken cancellationToken = default) =>
        await context.PaymentWebhookEvents.AddAsync(webhookEvent, cancellationToken);
}

/// <summary>Recognises the two ways a payment save can lose a race: the row's concurrency token changed, or a unique index was hit.</summary>
internal sealed class PersistenceConflicts(AppDbContext context) : IPersistenceConflicts
{
    public bool IsConflict(Exception exception) =>
        exception is DbUpdateConcurrencyException ||
        exception is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } };

    public void DiscardChanges() => context.ChangeTracker.Clear();
}
