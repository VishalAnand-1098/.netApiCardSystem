using System.Linq.Expressions;
using InviteCraft.Domain.Common;

namespace InviteCraft.Application.Common.Interfaces;

/// <summary>
/// Generic repository for simple aggregates. Query-heavy modules get specific repositories
/// (e.g. ITemplateRepository) that return projections instead of exposing IQueryable.
/// Changes are persisted with <see cref="IUnitOfWork.SaveChangesAsync"/>.
/// </summary>
public interface IRepository<T> where T : Entity
{
    Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<T>> ListAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);

    Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default);

    Task AddAsync(T entity, CancellationToken cancellationToken = default);

    void Update(T entity);

    /// <summary>Soft-deletable entities are hidden (not physically deleted) by the persistence layer.</summary>
    void Remove(T entity);
}

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
