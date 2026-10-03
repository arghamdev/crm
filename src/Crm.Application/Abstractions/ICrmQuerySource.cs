namespace Crm.Application.Abstractions;

/// <summary>
/// Read-only, composable queries for list screens. Filters, ordering and paging written against these
/// IQueryables run inside the database for the EF store (instead of loading whole tables), and over the
/// in-memory lists for the sample store. Keep predicates translatable: plain comparisons, Contains on local
/// collections, string Contains for search; no calls into domain methods.
/// </summary>
public interface ICrmQuerySource
{
    IQueryable<T> Query<T>() where T : class;
    Task<List<T>> ToListAsync<T>(IQueryable<T> query, CancellationToken cancellationToken = default);
    Task<int> CountAsync<T>(IQueryable<T> query, CancellationToken cancellationToken = default);
    Task<double?> AverageAsync(IQueryable<int> query, CancellationToken cancellationToken = default);
}

public sealed record PageRequest(int Page, int PageSize)
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 200;

    public static PageRequest Of(int page, int pageSize) =>
        new(Math.Max(1, page), Math.Clamp(pageSize <= 0 ? DefaultPageSize : pageSize, 1, MaxPageSize));

    public int Skip => (Page - 1) * PageSize;
}
