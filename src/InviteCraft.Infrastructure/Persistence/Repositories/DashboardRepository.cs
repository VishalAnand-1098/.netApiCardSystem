using InviteCraft.Application.Common.Models;
using InviteCraft.Application.Features.Dashboard;
using InviteCraft.Domain.Entities.Invitations;
using InviteCraft.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace InviteCraft.Infrastructure.Persistence.Repositories;

internal sealed class DashboardRepository(AppDbContext context) : IDashboardRepository
{
    private IQueryable<OrderListItemDto> OrderRows(Guid userId) =>
        context.Orders.AsNoTracking().Where(o => o.UserId == userId).OrderByDescending(o => o.CreatedAt)
            .Select(o => new OrderListItemDto(o.Id, o.OrderNumber, o.Status, o.TotalAmount, o.Coupon != null ? o.Coupon.Code : null, o.CreatedAt, o.PaidAt,
                o.Items.Select(i => (Guid?)i.InvitationId).FirstOrDefault(),
                o.Items.Select(i => i.Invitation.Title).FirstOrDefault(),
                o.Items.Select(i => i.TemplateName).FirstOrDefault(),
                o.Items.Select(i => i.PackageName).FirstOrDefault()));

    public async Task<DashboardCounts> GetCountsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var invitations = context.Invitations.AsNoTracking().Where(i => i.UserId == userId);
        var paid = context.Orders.AsNoTracking().Where(o => o.UserId == userId && o.Status == OrderStatus.Paid);
        return new DashboardCounts(
            await invitations.CountAsync(cancellationToken),
            await invitations.CountAsync(i => i.Status == InvitationStatus.Draft, cancellationToken),
            await invitations.CountAsync(i => i.UnlockedPackageId != null, cancellationToken),
            await paid.CountAsync(cancellationToken),
            await paid.SumAsync(o => (decimal?)o.TotalAmount, cancellationToken) ?? 0m,
            await context.Downloads.CountAsync(d => d.UserId == userId && d.Status == DownloadStatus.Ready, cancellationToken),
            await context.SavedTemplates.CountAsync(s => s.UserId == userId, cancellationToken));
    }

    public async Task<IReadOnlyList<OrderListItemDto>> GetRecentOrdersAsync(Guid userId, int take, CancellationToken cancellationToken = default) =>
        await OrderRows(userId).Take(take).ToListAsync(cancellationToken);

    public Task<PagedResult<OrderListItemDto>> ListOrdersAsync(Guid userId, OrderQuery query, CancellationToken cancellationToken = default)
    {
        var source = context.Orders.AsNoTracking().Where(o => o.UserId == userId);
        if (query.Status is { } status)
            source = source.Where(o => o.Status == status);
        return source.OrderByDescending(o => o.CreatedAt)
            .Select(o => new OrderListItemDto(o.Id, o.OrderNumber, o.Status, o.TotalAmount, o.Coupon != null ? o.Coupon.Code : null, o.CreatedAt, o.PaidAt,
                o.Items.Select(i => (Guid?)i.InvitationId).FirstOrDefault(),
                o.Items.Select(i => i.Invitation.Title).FirstOrDefault(),
                o.Items.Select(i => i.TemplateName).FirstOrDefault(),
                o.Items.Select(i => i.PackageName).FirstOrDefault()))
            .ToPagedResultAsync(query, cancellationToken);
    }

    public Task<PagedResult<PaymentListItemDto>> ListPaymentsAsync(Guid userId, PagedRequest query, CancellationToken cancellationToken = default) =>
        context.Payments.AsNoTracking().Where(p => p.Order.UserId == userId).OrderByDescending(p => p.CreatedAt)
            .Select(p => new PaymentListItemDto(p.Id, p.OrderId, p.Order.OrderNumber, p.GatewayPaymentId, p.Status, p.Method, p.Amount, p.Currency, p.CreatedAt, p.CapturedAt))
            .ToPagedResultAsync(query, cancellationToken);

    public Task<PagedResult<SavedTemplateRow>> ListSavedAsync(Guid userId, PagedRequest query, CancellationToken cancellationToken = default) =>
        context.SavedTemplates.AsNoTracking().Where(s => s.UserId == userId && s.Template.Status == TemplateStatus.Published).OrderByDescending(s => s.CreatedAt)
            .Select(s => new SavedTemplateRow(s.TemplateId, s.Template.Name, s.Template.Slug, s.Template.Tier, s.Template.Category.Name, s.Template.StartingPrice,
                s.Template.Assets.Where(a => a.AssetType == TemplateAssetType.Thumbnail).Select(a => (Guid?)a.Id).FirstOrDefault(), s.CreatedAt))
            .ToPagedResultAsync(query, cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetSavedIdsAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await context.SavedTemplates.AsNoTracking().Where(s => s.UserId == userId).Select(s => s.TemplateId).ToListAsync(cancellationToken);

    public Task<bool> IsPublishedAsync(Guid templateId, CancellationToken cancellationToken = default) =>
        context.Templates.AnyAsync(t => t.Id == templateId && t.Status == TemplateStatus.Published, cancellationToken);

    public Task<int> CountSavedAsync(Guid userId, CancellationToken cancellationToken = default) =>
        context.SavedTemplates.CountAsync(s => s.UserId == userId, cancellationToken);

    public async Task<bool> AddSavedAsync(Guid userId, Guid templateId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (await context.SavedTemplates.AnyAsync(s => s.UserId == userId && s.TemplateId == templateId, cancellationToken))
            return false;
        context.SavedTemplates.Add(new SavedTemplate { UserId = userId, TemplateId = templateId, CreatedAt = now });
        return true;
    }

    public async Task RemoveSavedAsync(Guid userId, Guid templateId, CancellationToken cancellationToken = default)
    {
        var existing = await context.SavedTemplates.FirstOrDefaultAsync(s => s.UserId == userId && s.TemplateId == templateId, cancellationToken);
        if (existing is not null)
            context.SavedTemplates.Remove(existing);
    }
}
