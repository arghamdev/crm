using Crm.Domain.Customers;

namespace Crm.Application.Abstractions;

public sealed record CustomerQuerySpec(
    string CompanyId,
    string? SelectedBranchId,
    string? SelectedTerritoryId,
    bool CompanyWide,
    IReadOnlyList<string> AllowedBranchIds,
    IReadOnlyList<string> AllowedTerritoryIds,
    string? Query,
    int Page,
    int PageSize);

public sealed record CustomerQueryPage(
    IReadOnlyList<Customer> Items,
    int TotalCount,
    IReadOnlySet<Guid> CustomerIdsWithActiveContacts,
    IReadOnlySet<Guid> CustomerIdsWithActiveAddresses);

public interface ICustomerQueryStore
{
    CustomerQueryPage Search(CustomerQuerySpec spec);
}
