using Crm.Application.Contracts;
using Crm.Domain.Accounts;
using Crm.Domain.Customers;
using Crm.Domain.Sales;

namespace Crm.Web.Presentation;

/// <summary>Lead list workspace: the service result plus the URL state, branch names and the chosen layout.</summary>
public sealed record LeadListPage(LeadListDto List, ListState State, IReadOnlyDictionary<string, string> BranchNames, string Layout, DateTimeOffset NowUtc)
{
    public const string Target = "leadList";
    public const string FormId = "leadFilters";
    public bool CanSelect => List.CanAssign || List.CanUpdate;

    public static LeadListPage Create(LeadListDto list, IReadOnlyList<OrganizationUnitOptionDto> branches, string? layout, DateTimeOffset nowUtc)
    {
        var cards = layout == "cards";
        var state = new ListState("/leads", "/leads/table", new Dictionary<string, string?>
        {
            ["view"] = list.View == "all" ? null : list.View,
            ["q"] = list.Query,
            ["status"] = list.Status?.ToString(),
            ["branchId"] = list.BranchId,
            ["source"] = list.Source,
            ["owner"] = list.OwnerUserId?.ToString(),
            ["includeClosed"] = list.IncludeClosed ? "true" : null,
            ["sort"] = list.Sort == "sla" ? null : list.Sort,
            ["layout"] = cards ? "cards" : null,
            ["pageSize"] = list.PageSize == 10 ? null : list.PageSize.ToString(),
            ["page"] = list.Page > 1 ? list.Page.ToString() : null
        });
        return new LeadListPage(list, state, branches.ToDictionary(x => x.Id, x => x.Name, StringComparer.OrdinalIgnoreCase), cards ? "cards" : "table", nowUtc);
    }

    public string BranchName(string branchId) => BranchNames.TryGetValue(branchId, out var name) ? name : branchId;

    /// <summary>The next step shown in the list: the planned next action, or the first contact while it is still pending.</summary>
    public static (string Text, DateTimeOffset? DueAtUtc, bool Planned) NextStep(LeadDto lead) =>
        lead.NextAction is { } action ? (action, lead.NextActionAtUtc, true)
        : lead.FirstContactAtUtc is null && lead.Status is not (LeadStatus.Converted or LeadStatus.Disqualified or LeadStatus.Duplicate or LeadStatus.Invalid)
            ? ("تماس اولیه", lead.FirstContactDueAtUtc, true)
            : ("تعیین نشده", null, false);
}

/// <summary>Customer/account list workspace.</summary>
public sealed record CustomerListPage(CustomerListDto List, ListState State, IReadOnlyList<OrganizationUnitOptionDto> Branches, string Layout, DateTimeOffset NowUtc)
{
    public const string Target = "customerList";
    public const string FormId = "customerFilters";

    public static CustomerListPage Create(CustomerListDto list, IReadOnlyList<OrganizationUnitOptionDto> branches, string? layout, DateTimeOffset nowUtc)
    {
        var cards = layout == "cards";
        var state = new ListState("/customers", "/customers/table", new Dictionary<string, string?>
        {
            ["view"] = list.View == "all" ? null : list.View,
            ["q"] = list.Query,
            ["branchId"] = list.BranchId,
            ["kind"] = list.Kind?.ToString(),
            ["relationship"] = list.Relationship?.ToString(),
            ["segment"] = list.Segment,
            ["status"] = list.Status?.ToString(),
            ["owner"] = list.Owner,
            ["sort"] = list.Sort == "recent" ? null : list.Sort,
            ["layout"] = cards ? "cards" : null,
            ["pageSize"] = list.PageSize == 10 ? null : list.PageSize.ToString(),
            ["page"] = list.Page > 1 ? list.Page.ToString() : null
        });
        return new CustomerListPage(list, state, branches, cards ? "cards" : "table", nowUtc);
    }
}

public static class LeadLabels
{
    public static string Status(LeadStatus value) => value switch
    {
        LeadStatus.New => "جدید", LeadStatus.Assigned => "ارجاع‌شده", LeadStatus.Contacted => "در حال پیگیری", LeadStatus.Qualified => "واجد شرایط",
        LeadStatus.Nurture => "پرورش", LeadStatus.Disqualified => "ردشده", LeadStatus.Duplicate => "تکراری", LeadStatus.Invalid => "نامعتبر", _ => "تبدیل‌شده"
    };

    public static string StatusTone(LeadStatus value) => value switch
    {
        LeadStatus.New => "info", LeadStatus.Assigned => "violet", LeadStatus.Contacted => "warning", LeadStatus.Qualified => "success",
        LeadStatus.Nurture => "neutral", LeadStatus.Converted => "primary", _ => "danger"
    };

    public static string StatusIcon(LeadStatus value) => value switch
    {
        LeadStatus.New => "i-plus", LeadStatus.Assigned => "i-user", LeadStatus.Contacted => "i-phone", LeadStatus.Qualified => "i-check",
        LeadStatus.Nurture => "i-clock", LeadStatus.Duplicate => "i-copy", LeadStatus.Converted => "i-target", _ => "i-close"
    };
}

public static class CustomerLabels
{
    public static string Status(CustomerStatus value) => value switch
    {
        CustomerStatus.Active => "فعال", CustomerStatus.Inactive => "غیرفعال", _ => "در حال بررسی"
    };

    public static string StatusTone(CustomerStatus value) => value switch
    {
        CustomerStatus.Active => "success", CustomerStatus.Inactive => "neutral", _ => "warning"
    };

    public static string StatusIcon(CustomerStatus value) => value switch
    {
        CustomerStatus.Active => "i-check", CustomerStatus.Inactive => "i-close", _ => "i-clock"
    };

    public static string Kind(CustomerKind value) => value == CustomerKind.Legal ? "حقوقی" : "حقیقی";

    public static string RelationshipIcon(AccountRelationship value) => value switch
    {
        AccountRelationship.Prospect => "i-target", AccountRelationship.Supplier => "i-store", AccountRelationship.Partner => "i-users", _ => "i-building"
    };

    public static string ActivityIcon(ActivityType? value) => value switch
    {
        ActivityType.Call => "i-phone", ActivityType.Meeting => "i-calendar", _ => "i-task"
    };
}
