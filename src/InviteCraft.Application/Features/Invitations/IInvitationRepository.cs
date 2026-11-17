using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Application.Common.Models;
using InviteCraft.Domain.Entities.Invitations;

namespace InviteCraft.Application.Features.Invitations;

public interface IInvitationRepository : IRepository<Invitation>
{
    /// <summary>Tracked. Null when the invitation does not exist, is deleted, or belongs to another user.</summary>
    Task<Invitation?> GetOwnedAsync(Guid userId, Guid id, bool includeValues, CancellationToken cancellationToken = default);

    Task<int> CountOwnedAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<PagedResult<InvitationListRow>> SearchOwnedAsync(Guid userId, InvitationQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the invitation row as modified so its concurrency token (xmin) and UpdatedAt change even when only
    /// child rows (field values) changed — that is what makes stale saves from another tab detectable.
    /// </summary>
    void Touch(Invitation invitation);

    /// <summary>Tracked, found by share slug regardless of owner (visibility is checked by the caller).</summary>
    Task<Invitation?> GetBySlugAsync(string slug, bool includeValues, CancellationToken cancellationToken = default);

    /// <summary>Counts one public view without touching the tracked row (atomic, no concurrency-token bump).</summary>
    Task IncrementViewCountAsync(Guid id, CancellationToken cancellationToken = default);
}
