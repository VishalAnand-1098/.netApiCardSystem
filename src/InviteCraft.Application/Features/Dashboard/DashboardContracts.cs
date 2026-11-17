using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Application.Common.Models;
using InviteCraft.Application.Features.Invitations;
using InviteCraft.Application.Features.Templates;
using InviteCraft.Domain.Enums;

namespace InviteCraft.Application.Features.Dashboard;

public sealed record DashboardCounts(int Invitations, int Drafts, int Unlocked, int PaidOrders, decimal TotalSpent, int Downloads, int Saved);

public sealed record DashboardDto(
    DashboardCounts Counts,
    IReadOnlyList<InvitationListItemDto> RecentInvitations,
    IReadOnlyList<OrderListItemDto> RecentOrders);

/// <summary>?status=&amp;page=&amp;pageSize=</summary>
public sealed record OrderQuery : PagedRequest
{
    public OrderStatus? Status { get; init; }
}

public sealed record OrderListItemDto(
    Guid Id, string OrderNumber, OrderStatus Status, decimal TotalAmount, string? CouponCode, DateTimeOffset CreatedAt, DateTimeOffset? PaidAt,
    Guid? InvitationId, string? InvitationTitle, string? TemplateName, string? PackageName);

public sealed record PaymentListItemDto(
    Guid Id, Guid OrderId, string OrderNumber, string? GatewayPaymentId, PaymentStatus Status, string? Method, decimal Amount, string Currency,
    DateTimeOffset CreatedAt, DateTimeOffset? CapturedAt);

public sealed record SavedTemplateRow(Guid TemplateId, string Name, string Slug, TemplateTier Tier, string CategoryName, decimal? StartingPrice, Guid? ThumbnailAssetId, DateTimeOffset SavedAt);

public sealed record SavedTemplateDto(Guid TemplateId, string Name, string Slug, TemplateTier Tier, string CategoryName, decimal? StartingPrice, string? ThumbnailUrl, DateTimeOffset SavedAt);

public interface IDashboardRepository
{
    Task<DashboardCounts> GetCountsAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrderListItemDto>> GetRecentOrdersAsync(Guid userId, int take, CancellationToken cancellationToken = default);
    Task<PagedResult<OrderListItemDto>> ListOrdersAsync(Guid userId, OrderQuery query, CancellationToken cancellationToken = default);
    Task<PagedResult<PaymentListItemDto>> ListPaymentsAsync(Guid userId, PagedRequest query, CancellationToken cancellationToken = default);
    Task<PagedResult<SavedTemplateRow>> ListSavedAsync(Guid userId, PagedRequest query, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Guid>> GetSavedIdsAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<bool> IsPublishedAsync(Guid templateId, CancellationToken cancellationToken = default);
    Task<int> CountSavedAsync(Guid userId, CancellationToken cancellationToken = default);
    /// <summary>Idempotent. Returns true when a new bookmark was added.</summary>
    Task<bool> AddSavedAsync(Guid userId, Guid templateId, DateTimeOffset now, CancellationToken cancellationToken = default);
    /// <summary>Idempotent.</summary>
    Task RemoveSavedAsync(Guid userId, Guid templateId, CancellationToken cancellationToken = default);
}

public interface IDashboardService
{
    Task<DashboardDto> GetAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<PagedResult<OrderListItemDto>> ListOrdersAsync(Guid userId, OrderQuery query, CancellationToken cancellationToken = default);
    Task<PagedResult<PaymentListItemDto>> ListPaymentsAsync(Guid userId, PagedRequest query, CancellationToken cancellationToken = default);
    Task<PagedResult<SavedTemplateDto>> ListSavedAsync(Guid userId, PagedRequest query, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Guid>> GetSavedIdsAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<Result> SaveAsync(Guid userId, Guid templateId, CancellationToken cancellationToken = default);
    Task<Result> UnsaveAsync(Guid userId, Guid templateId, CancellationToken cancellationToken = default);
}

internal sealed class DashboardService(
    IDashboardRepository repository, IInvitationRepository invitations, IUnitOfWork unitOfWork, TemplateMapper mapper, TimeProvider timeProvider) : IDashboardService
{
    public const int MaxSaved = 200;

    public async Task<DashboardDto> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var counts = await repository.GetCountsAsync(userId, cancellationToken);
        var recent = await invitations.SearchOwnedAsync(userId, new InvitationQuery { PageSize = 4 }, cancellationToken);
        var items = recent.Items.Select(r => new InvitationListItemDto(r.Id, r.Title, r.Status, r.TemplateName, r.TemplateSlug,
            mapper.ThumbUrl(r.TemplateId, r.ThumbnailAssetId), r.IsUnlocked, r.UpdatedAt)).ToList();
        return new DashboardDto(counts, items, await repository.GetRecentOrdersAsync(userId, 3, cancellationToken));
    }

    public Task<PagedResult<OrderListItemDto>> ListOrdersAsync(Guid userId, OrderQuery query, CancellationToken cancellationToken = default) =>
        repository.ListOrdersAsync(userId, query, cancellationToken);

    public Task<PagedResult<PaymentListItemDto>> ListPaymentsAsync(Guid userId, PagedRequest query, CancellationToken cancellationToken = default) =>
        repository.ListPaymentsAsync(userId, query, cancellationToken);

    public async Task<PagedResult<SavedTemplateDto>> ListSavedAsync(Guid userId, PagedRequest query, CancellationToken cancellationToken = default)
    {
        var page = await repository.ListSavedAsync(userId, query, cancellationToken);
        return new PagedResult<SavedTemplateDto>(
            page.Items.Select(r => new SavedTemplateDto(r.TemplateId, r.Name, r.Slug, r.Tier, r.CategoryName, r.StartingPrice, mapper.ThumbUrl(r.TemplateId, r.ThumbnailAssetId), r.SavedAt)).ToList(),
            page.Page, page.PageSize, page.TotalCount);
    }

    public Task<IReadOnlyList<Guid>> GetSavedIdsAsync(Guid userId, CancellationToken cancellationToken = default) =>
        repository.GetSavedIdsAsync(userId, cancellationToken);

    public async Task<Result> SaveAsync(Guid userId, Guid templateId, CancellationToken cancellationToken = default)
    {
        if (!await repository.IsPublishedAsync(templateId, cancellationToken))
            return TemplateErrors.NotFound;
        if (await repository.CountSavedAsync(userId, cancellationToken) >= MaxSaved)
            return Error.Conflict("Saved.LimitReached", $"You can save up to {MaxSaved} designs. Remove some to save more.");

        if (await repository.AddSavedAsync(userId, templateId, timeProvider.GetUtcNow(), cancellationToken))
            await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> UnsaveAsync(Guid userId, Guid templateId, CancellationToken cancellationToken = default)
    {
        await repository.RemoveSavedAsync(userId, templateId, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
