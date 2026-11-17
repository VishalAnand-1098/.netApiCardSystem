using System.Linq.Expressions;
using InviteCraft.Application.Common.Interfaces;
using InviteCraft.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace InviteCraft.Infrastructure.Persistence.Repositories;

/// <summary>EF Core implementation of <see cref="IRepository{T}"/>. Specific repositories derive from this.</summary>
internal class Repository<T>(AppDbContext context) : IRepository<T> where T : Entity
{
    protected AppDbContext Context { get; } = context;
    protected DbSet<T> Set => Context.Set<T>();

    public virtual Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Set.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public virtual async Task<IReadOnlyList<T>> ListAsync(
        Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default)
    {
        var query = predicate is null ? Set.AsNoTracking() : Set.AsNoTracking().Where(predicate);
        return await query.ToListAsync(cancellationToken);
    }

    public Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) =>
        Set.AnyAsync(predicate, cancellationToken);

    public Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default) =>
        predicate is null ? Set.CountAsync(cancellationToken) : Set.CountAsync(predicate, cancellationToken);

    public async Task AddAsync(T entity, CancellationToken cancellationToken = default) =>
        await Set.AddAsync(entity, cancellationToken);

    public void Update(T entity) => Set.Update(entity);

    public void Remove(T entity) => Set.Remove(entity);
}
