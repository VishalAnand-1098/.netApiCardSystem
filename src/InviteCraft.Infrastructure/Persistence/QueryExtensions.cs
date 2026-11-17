using System.Linq.Expressions;
using InviteCraft.Application.Common.Models;
using Microsoft.EntityFrameworkCore;

namespace InviteCraft.Infrastructure.Persistence;

internal static class QueryExtensions
{
    public const string LikeEscape = "\\";

    /// <summary>"%term%" for ILIKE with %, _ and \ escaped, so user input is matched literally.</summary>
    public static string ContainsPattern(string term) =>
        "%" + term.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

    public static IOrderedQueryable<T> OrderBy<T, TKey>(this IQueryable<T> query, Expression<Func<T, TKey>> key, SortDirection direction) =>
        direction == SortDirection.Desc ? query.OrderByDescending(key) : query.OrderBy(key);

    /// <summary>Counts, then fetches one page — two round-trips, both index-friendly.</summary>
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> query, PagedRequest request, CancellationToken cancellationToken)
    {
        var total = await query.CountAsync(cancellationToken);
        if (total == 0)
            return PagedResult<T>.Empty(request.Page, request.PageSize);

        var items = await query.Skip(request.Skip).Take(request.PageSize).ToListAsync(cancellationToken);
        return new PagedResult<T>(items, request.Page, request.PageSize, total);
    }
}
