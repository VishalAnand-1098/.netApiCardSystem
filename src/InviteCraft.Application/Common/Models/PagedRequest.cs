namespace InviteCraft.Application.Common.Models;

public enum SortDirection
{
    Asc,
    Desc
}

/// <summary>
/// Base query for every list endpoint. Bound from the query string:
/// <c>?page=2&amp;pageSize=24&amp;search=royal&amp;sortBy=popularity&amp;sortDir=desc</c>.
/// Feature queries derive from this and add their own filters.
/// Values are clamped rather than rejected so a bad link never breaks a page.
/// </summary>
public record PagedRequest
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    private readonly int _page = 1;
    private readonly int _pageSize = DefaultPageSize;

    public int Page
    {
        get => _page;
        init => _page = value < 1 ? 1 : value;
    }

    public int PageSize
    {
        get => _pageSize;
        init => _pageSize = value < 1 ? DefaultPageSize : Math.Min(value, MaxPageSize);
    }

    public string? Search { get; init; }

    /// <summary>Whitelisted per endpoint; unknown values fall back to the endpoint default.</summary>
    public string? SortBy { get; init; }

    public SortDirection SortDir { get; init; } = SortDirection.Asc;

    public int Skip => (Page - 1) * PageSize;
}
