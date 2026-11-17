using InviteCraft.Api.Contracts;
using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Application.Features.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InviteCraft.Api.Controllers;

/// <summary>The signed-in customer's in-app notifications (payment received, refunds, …).</summary>
[Route(ApiRoutes.V1 + "/notifications")]
[Authorize]
public sealed class NotificationsController(INotificationService notifications, ICurrentUser currentUser) : ApiControllerBase
{
    private Guid UserId => currentUser.UserId!.Value;

    /// <summary>Newest first. ?unreadOnly=&amp;page=&amp;pageSize=</summary>
    [HttpGet]
    [ProducesResponseType<ApiResponse<IReadOnlyList<NotificationDto>>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<NotificationDto>>>> List([FromQuery] NotificationQuery query, CancellationToken cancellationToken) =>
        PagedResponse(await notifications.ListAsync(UserId, query, cancellationToken));

    /// <summary>How many are unread — for the bell badge.</summary>
    [HttpGet("unread-count")]
    [ProducesResponseType<ApiResponse<int>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<int>>> UnreadCount(CancellationToken cancellationToken) =>
        OkResponse(await notifications.CountUnreadAsync(UserId, cancellationToken));

    /// <summary>Marks the given notifications read; an empty body marks everything read. Idempotent.</summary>
    [HttpPost("read")]
    [ProducesResponseType<ApiResponse<int>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<int>>> MarkRead(MarkReadRequest request, CancellationToken cancellationToken) =>
        OkResponse(await notifications.MarkReadAsync(UserId, request, cancellationToken), "Marked as read.");
}
