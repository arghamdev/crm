using Crm.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Crm.Infrastructure.Data;

public sealed class EfCoreCustomerQueryStore(CrmDbContext db) : ICustomerQueryStore
{
    public CustomerQueryPage Search(CustomerQuerySpec spec)
    {
        var query = db.Customers.AsNoTracking().Where(x => x.CompanyId == spec.CompanyId);
        if (spec.SelectedBranchId is not null) query = query.Where(x => x.BranchId == spec.SelectedBranchId);
        if (spec.SelectedTerritoryId is not null) query = query.Where(x => x.TerritoryId == spec.SelectedTerritoryId);
        if (!spec.CompanyWide)
        {
            var branches = spec.AllowedBranchIds.ToArray();
            var territories = spec.AllowedTerritoryIds.ToArray();
            query = query.Where(x => branches.Contains(x.BranchId) || x.TerritoryId != null && territories.Contains(x.TerritoryId));
        }
        if (!string.IsNullOrWhiteSpace(spec.Query))
        {
            var term = spec.Query.Trim();
            query = query.Where(x => x.Name.Contains(term) || x.Code.Contains(term));
        }
        var total = query.Count();
        var items = query.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .Skip((spec.Page - 1) * spec.PageSize).Take(spec.PageSize).ToList();
        var ids = items.Select(x => x.Id).ToArray();
        var contactIds = db.CustomerContacts.AsNoTracking().Where(x => ids.Contains(x.CustomerId) && x.IsActive)
            .Select(x => x.CustomerId).Distinct().ToHashSet();
        var addressIds = db.CustomerAddresses.AsNoTracking().Where(x => ids.Contains(x.CustomerId) && x.IsActive)
            .Select(x => x.CustomerId).Distinct().ToHashSet();
        return new CustomerQueryPage(items, total, contactIds, addressIds);
    }
}
