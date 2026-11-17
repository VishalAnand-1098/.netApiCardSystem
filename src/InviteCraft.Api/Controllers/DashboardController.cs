using InviteCraft.Api.Contracts;
using InviteCraft.Api.Infrastructure;
using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Application.Common.Models;
using InviteCraft.Application.Features.Dashboard;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InviteCraft.Api.Controllers;

/// <summary>The signed-in customer's account overview, order and payment history, and saved (bookmarked) templates.</summary>
[Route(ApiRoutes.V1)]
[Authorize]
public sealed class DashboardController(IDashboardService dashboard, ICurrentUser currentUser) : ApiControllerBase
{
    private Guid UserId => currentUser.UserId!.Value;

    /// <summary>Counts (invitations, drafts, unlocked, paid orders, total spent, downloads, saved) plus recent invitations and orders.</summary>
    [HttpGet("account/dashboard")]
    [ProducesResponseType<ApiResponse<DashboardDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<DashboardDto>>> Get(CancellationToken cancellationToken) =>
        Ok(ApiResponse.Ok(await dashboard.GetAsync(UserId, cancellationToken), traceId: TraceId));

    /// <summary>My orders, newest first. ?status=Pending|AwaitingPayment|Paid|Failed|Cancelled|Expired|Refunded&amp;page=&amp;pageSize=. Detail: GET /orders/{id}.</summary>
    [HttpGet("orders")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<OrderListItemDto>>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<OrderListItemDto>>>> Orders([FromQuery] OrderQuery query, CancellationToken cancellationToken) =>
        PagedResponse(await dashboard.ListOrdersAsync(UserId, query, cancellationToken));

    /// <summary>My payment attempts, newest first.</summary>
    [HttpGet("payments")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<PaymentListItemDto>>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PaymentListItemDto>>>> Payments([FromQuery] PagedRequest query, CancellationToken cancellationToken) =>
        PagedResponse(await dashboard.ListPaymentsAsync(UserId, query, cancellationToken));

    /// <summary>Templates I bookmarked (published ones only), newest first.</summary>
    [HttpGet("saved-templates")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<SavedTemplateDto>>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SavedTemplateDto>>>> Saved([FromQuery] PagedRequest query, CancellationToken cancellationToken) =>
        PagedResponse(await dashboard.ListSavedAsync(UserId, query, cancellationToken));

    /// <summary>Ids of every template I saved — lets the storefront paint the heart buttons.</summary>
    [HttpGet("saved-templates/ids")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<Guid>>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<Guid>>>> SavedIds(CancellationToken cancellationToken) =>
        Ok(ApiResponse.Ok(await dashboard.GetSavedIdsAsync(UserId, cancellationToken), traceId: TraceId));

    /// <summary>Bookmarks a published template. Idempotent; 404 for unknown/unpublished templates, 409 past 200 saved.</summary>
    [HttpPut("saved-templates/{templateId:guid}")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Save(Guid templateId, CancellationToken cancellationToken) =>
        FromResult(await dashboard.SaveAsync(UserId, templateId, cancellationToken), "Saved to your favourites.");

    /// <summary>Removes a bookmark. Idempotent.</summary>
    [HttpDelete("saved-templates/{templateId:guid}")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    public async Task<ActionResult> Unsave(Guid templateId, CancellationToken cancellationToken) =>
        FromResult(await dashboard.UnsaveAsync(UserId, templateId, cancellationToken), "Removed from your favourites.");
}
