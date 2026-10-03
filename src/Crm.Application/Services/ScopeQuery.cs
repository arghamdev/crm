using System.Linq.Expressions;
using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Domain.Organization;

namespace Crm.Application.Services;

/// <summary>
/// Expresses the permission + organization scope check (the same rule as AccessSnapshot.AllowsRecord combined
/// with the selected company/branch/territory) as a translatable predicate, so the database filters rows
/// instead of the application loading a table and filtering it in memory.
/// </summary>
public static class ScopeQuery
{
    public static IQueryable<T> InScope<T>(this IQueryable<T> query, AccessSnapshot snapshot,
        OrganizationSelection organization, string permission) where T : class, IOrganizationScoped
    {
        var grants = snapshot.PermissionScopeGrants.Where(x =>
            Same(x.CompanyId, organization.CompanyId) && Same(x.Permission, permission)).ToList();
        if (grants.Count == 0) return query.Where(False<T>());

        var x = Expression.Parameter(typeof(T), "x");
        var company = Property(x, nameof(IOrganizationScoped.CompanyId));
        var branch = Property(x, nameof(IOrganizationScoped.BranchId));
        var territory = Property(x, nameof(IOrganizationScoped.TerritoryId));

        Expression body = Expression.Equal(company, Expression.Constant(organization.CompanyId));
        if (organization.BranchId is not null)
            body = Expression.AndAlso(body, Expression.Equal(branch, Expression.Constant(organization.BranchId)));
        if (organization.TerritoryId is not null)
            body = Expression.AndAlso(body, Expression.Equal(territory, Expression.Constant(organization.TerritoryId, typeof(string))));

        if (!grants.Any(g => Same(g.ScopeType, "Company")))
        {
            var branches = grants.Where(g => Same(g.ScopeType, "Branch")).Select(g => g.ScopeId).Distinct().ToArray();
            var territories = grants.Where(g => Same(g.ScopeType, "Territory")).Select(g => g.ScopeId).Distinct().ToArray();
            Expression allowed = Expression.Constant(false);
            if (branches.Length > 0) allowed = Expression.OrElse(allowed, In(branch, branches));
            if (territories.Length > 0)
                allowed = Expression.OrElse(allowed, Expression.AndAlso(
                    Expression.NotEqual(territory, Expression.Constant(null, typeof(string))), In(territory, territories)));
            body = Expression.AndAlso(body, allowed);
        }
        return query.Where(Expression.Lambda<Func<T, bool>>(body, x));
    }

    public static async Task<PagedResult<TResult>> ToPageAsync<T, TResult>(this IQueryable<T> ordered, ICrmQuerySource source,
        PageRequest page, Func<T, TResult> map, string? query, CancellationToken cancellationToken = default)
    {
        var total = await source.CountAsync(ordered, cancellationToken);
        var lastPage = Math.Max(1, (int)Math.Ceiling(total / (double)page.PageSize));
        var actual = page with { Page = Math.Min(page.Page, lastPage) };
        var rows = await source.ToListAsync(ordered.Skip(actual.Skip).Take(actual.PageSize), cancellationToken);
        return new PagedResult<TResult>(rows.Select(map).ToList(), actual.Page, actual.PageSize, total, query);
    }

    private static Expression<Func<T, bool>> False<T>() => _ => false;

    private static MemberExpression Property(ParameterExpression parameter, string name) =>
        Expression.Property(parameter, parameter.Type.GetProperty(name) ??
            throw new InvalidOperationException($"{parameter.Type.Name} has no {name} property."));

    private static MethodCallExpression In(Expression member, string[] values) =>
        Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), [typeof(string)], Expression.Constant(values), member);

    private static bool Same(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
