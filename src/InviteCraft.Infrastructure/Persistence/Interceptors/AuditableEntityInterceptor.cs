using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace InviteCraft.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Stamps created/updated metadata and converts deletes of <see cref="ISoftDeletable"/> entities
/// into soft deletes, so application code never sets these fields by hand.
/// </summary>
internal sealed class AuditableEntityInterceptor(TimeProvider timeProvider, ICurrentUser currentUser) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext? context)
    {
        if (context is null)
            return;

        var now = timeProvider.GetUtcNow();
        var userId = currentUser.UserId;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry is { State: EntityState.Deleted, Entity: ISoftDeletable softDeletable })
            {
                entry.State = EntityState.Modified;
                softDeletable.MarkDeleted(now);
            }

            switch (entry.Entity)
            {
                case AuditableEntity auditable when entry.State == EntityState.Added:
                    auditable.CreatedAt = now;
                    auditable.CreatedBy ??= userId;
                    auditable.UpdatedAt = now;
                    auditable.UpdatedBy = userId;
                    break;

                case AuditableEntity auditable when entry.State == EntityState.Modified:
                    auditable.UpdatedAt = now;
                    auditable.UpdatedBy = userId;
                    // Creation metadata is immutable even if a detached entity was attached with defaults.
                    entry.Property(nameof(AuditableEntity.CreatedAt)).IsModified = false;
                    entry.Property(nameof(AuditableEntity.CreatedBy)).IsModified = false;
                    break;

                case CreationAuditedEntity created when entry.State == EntityState.Added && created.CreatedAt == default:
                    created.CreatedAt = now;
                    break;
            }
        }
    }
}
