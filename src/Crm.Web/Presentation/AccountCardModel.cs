using Crm.Application.Contracts;
using Crm.Domain.Customers;

namespace Crm.Web.Presentation;

/// <summary>Summary card of an account (identity on one side, contact details on the other), shared by the account page and the edit form.</summary>
public sealed record AccountCardModel(
    Guid Id,
    string Name,
    string Code,
    CustomerStatus Status,
    bool HasLogo,
    long Version,
    string? Industry,
    string? Region,
    string? Phone,
    string? Email,
    string? Website,
    string? Address,
    bool Heading = true)
{
    public string StatusLabel => Status switch { CustomerStatus.Active => "فعال", CustomerStatus.Inactive => "غیرفعال", _ => "در حال بررسی" };
    public string StatusTone => Status switch { CustomerStatus.Active => "success", CustomerStatus.Inactive => "neutral", _ => "warning" };

    public static AccountCardModel From(AccountSummaryDto a) => new(a.Id, a.Name, a.Code, a.Status, a.HasLogo, a.Version, a.Industry, a.Region,
        a.Phones.FirstOrDefault().Value, a.Email, a.Website, a.Address);

    public static AccountCardModel From(CustomerEditDto c)
    {
        var p = c.Profile;
        var address = string.Join("، ", new[] { p?.Province, c.City, p?.Address }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct());
        return new(c.Id, c.Name, c.Code, c.Status, c.HasLogo, c.ExpectedVersion, p?.ActivityType, string.IsNullOrWhiteSpace(p?.Province) ? c.City : p.Province,
            c.PrimaryPhone ?? p?.Phone1 ?? p?.Mobile1, c.PrimaryEmail?.ToLowerInvariant(), p?.Website, address.Length == 0 ? null : address, Heading: false);
    }
}
