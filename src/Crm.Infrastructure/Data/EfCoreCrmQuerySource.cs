using Crm.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Crm.Infrastructure.Data;

/// <summary>Translates list queries to SQL; nothing is loaded beyond the requested page and aggregates.</summary>
public sealed class EfCoreCrmQuerySource(CrmDbContext db) : ICrmQuerySource
{
    public IQueryable<T> Query<T>() where T : class => db.Set<T>().AsNoTracking();

    public Task<List<T>> ToListAsync<T>(IQueryable<T> query, CancellationToken cancellationToken = default) =>
        query.ToListAsync(cancellationToken);

    public Task<int> CountAsync<T>(IQueryable<T> query, CancellationToken cancellationToken = default) =>
        query.CountAsync(cancellationToken);

    public async Task<double?> AverageAsync(IQueryable<int> query, CancellationToken cancellationToken = default) =>
        await query.Select(x => (double?)x).AverageAsync(cancellationToken);

    public Task<decimal> SumAsync(IQueryable<decimal> query, CancellationToken cancellationToken = default) =>
        query.SumAsync(cancellationToken);
}
