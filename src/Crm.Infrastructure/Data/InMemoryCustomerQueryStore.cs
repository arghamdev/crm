using Crm.Application.Abstractions;
using Crm.Domain.Customers;

namespace Crm.Infrastructure.Data;

public sealed class InMemoryCustomerQueryStore(ICrmDataStore store) : ICustomerQueryStore
{
    public CustomerQueryPage Search(CustomerQuerySpec spec) => store.Read(data =>
    {
        IEnumerable<Customer> query = data.Customers.Where(x =>
            x.CompanyId.Equals(spec.CompanyId, StringComparison.OrdinalIgnoreCase));
        if (spec.SelectedBranchId is not null)
            query = query.Where(x => x.BranchId.Equals(spec.SelectedBranchId, StringComparison.OrdinalIgnoreCase));
        if (spec.SelectedTerritoryId is not null)
            query = query.Where(x => string.Equals(x.TerritoryId, spec.SelectedTerritoryId, StringComparison.OrdinalIgnoreCase));
        if (!spec.CompanyWide)
            query = query.Where(x => spec.AllowedBranchIds.Contains(x.BranchId, StringComparer.OrdinalIgnoreCase) ||
                x.TerritoryId is not null && spec.AllowedTerritoryIds.Contains(x.TerritoryId, StringComparer.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(spec.Query))
        {
            var term = spec.Query.Trim();
            query = query.Where(x => x.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                x.Code.Contains(term, StringComparison.OrdinalIgnoreCase));
        }
        var ordered = query.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id);
        var total = ordered.Count();
        var items = ordered.Skip((spec.Page - 1) * spec.PageSize).Take(spec.PageSize).ToList();
        var ids = items.Select(x => x.Id).ToHashSet();
        return new CustomerQueryPage(items, total,
            data.CustomerContacts.Where(x => ids.Contains(x.CustomerId) && x.IsActive).Select(x => x.CustomerId).ToHashSet(),
            data.CustomerAddresses.Where(x => ids.Contains(x.CustomerId) && x.IsActive).Select(x => x.CustomerId).ToHashSet());
    });
}
