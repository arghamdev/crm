using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Domain.Customers;
using Crm.Domain.Identity;
using Crm.Domain.Organization;

namespace Crm.Application.Services;

/// <summary>Permission names of the account file (پرونده حساب). Each area is checked separately on the server.</summary>
public static class AccountPermissions
{
    public const string ActivityRead = "Activity.Read", ActivityCreate = "Activity.Create", ActivityUpdate = "Activity.Update";
    public const string NoteRead = "Note.Read", NoteCreate = "Note.Create", NoteRestricted = "Note.Restricted.Read";
    public const string DocumentRead = "Document.Read", DocumentManage = "Document.Manage", DocumentSensitive = "Document.Sensitive.Read";
    public const string PaymentRead = "Payment.Read", PaymentCreate = "Payment.Create", PaymentApprove = "Payment.Approve";
    public const string BankRead = "BankAccount.Read", BankManage = "BankAccount.Manage";
    public const string GuaranteeRead = "Account.Guarantee.Read", GuaranteeManage = "Account.Guarantee.Manage";
    public const string ContractRead = "Contract.Read", ContractManage = "Contract.Manage";
    public const string ProjectRead = "Project.Read", ProjectManage = "Project.Manage";
    public const string CampaignRead = "Campaign.Read", CampaignManage = "Campaign.Manage";
    public const string SurveyRead = "Survey.Read", SurveyManage = "Survey.Manage";
    /// <summary>Linking/unlinking existing records (parent/child accounts, leads, memberships, participations, allocations).</summary>
    public const string RelationManage = "Account.Relation.Manage";
}

/// <summary>Account access rules shared by the account-file services: scope, permission, mutability and the change log.</summary>
public static class AccountGuard
{
    public static AccessSnapshot Snapshot(IAccessSnapshotService access, Guid userId) =>
        access.Get(userId) ?? throw new UnauthorizedAccessException("No active access snapshot was found.");

    public static bool Has(AccessSnapshot snapshot, string companyId, string permission) => snapshot.PermissionsFor(companyId).Contains(permission);

    public static void Require(AccessSnapshot snapshot, string companyId, string permission)
    {
        if (!Has(snapshot, companyId, permission)) throw new UnauthorizedAccessException($"{permission} permission is required.");
    }

    public static bool InContext(AccessSnapshot snapshot, OrganizationSelection organization, string permission, IOrganizationScoped entity) =>
        Same(entity.CompanyId, organization.CompanyId) && (organization.BranchId is null || Same(entity.BranchId, organization.BranchId)) &&
        (organization.TerritoryId is null || Same(entity.TerritoryId, organization.TerritoryId)) &&
        snapshot.AllowsRecord(entity.CompanyId, permission, entity.BranchId, entity.TerritoryId);

    /// <summary>
    /// The account if the user may read it in the selected context and, for the given area permission, holds that
    /// permission within the account's branch/territory. Anything else is "not found" or "forbidden", never partial data.
    /// </summary>
    public static Customer Account(CrmDataSet data, AccessSnapshot snapshot, OrganizationSelection organization, Guid accountId,
        string? permission = null)
    {
        var account = data.Find<Customer>(x => x.Id == accountId).SingleOrDefault(x => InContext(snapshot, organization, "Customer.Read", x)) ??
            throw new KeyNotFoundException("حساب در دامنهٔ جاری پیدا نشد.");
        if (permission is not null && !Allows(snapshot, account, permission))
            throw new UnauthorizedAccessException($"{permission} permission is required for this account.");
        return account;
    }

    public static bool Allows(AccessSnapshot snapshot, Customer account, string permission) =>
        Has(snapshot, account.CompanyId, permission) && snapshot.AllowsRecord(account.CompanyId, permission, account.BranchId, account.TerritoryId);

    public static void EnsureMutable(Customer account)
    {
        if (account.Status == CustomerStatus.Inactive)
            throw new InvalidOperationException("حساب غیرفعال است؛ ابتدا آن را فعال کنید یا ادغام را بازگردانید.");
    }

    /// <summary>Sales managers/supervisors see all sales records of the account; others only the ones they own.</summary>
    public static bool ManagerWide(AccessSnapshot snapshot, string companyId) => snapshot.ScopeGrants.Any(x => Same(x.CompanyId, companyId) &&
        (x.RoleKey.Equals("SalesManager", StringComparison.OrdinalIgnoreCase) || x.RoleKey.Equals("SalesSupervisor", StringComparison.OrdinalIgnoreCase)));

    /// <summary>Change log of the account ("تاریخچه تغییرات"): who changed what and when.</summary>
    public static void Log(CrmDataSet data, Customer account, CustomerTimelineType type, string title, string? description, Guid actorUserId,
        DateTimeOffset nowUtc, string? reference = null) =>
        data.Append(new CustomerTimelineEvent(Guid.NewGuid(), account.CompanyId, account.Id, type, title.Length <= 200 ? title : title[..200],
            description is { Length: > 2000 } ? description[..2000] : description ?? string.Empty, nowUtc, "CRM", reference, actorUserId));

    /// <summary>Active internal users who can own records of the account (company-wide roles or roles on its branch/territory).</summary>
    public static List<UserOptionDto> Owners(CrmDataSet data, Customer account, DateTimeOffset nowUtc)
    {
        var assignments = data.Find<UserRoleAssignment>(x => x.CompanyId == account.CompanyId && x.RoleKey != "DealerUser").Where(x => x.IsEffective(nowUtc) &&
            (Same(x.ScopeType, "Company") || Same(x.ScopeType, "Branch") && Same(x.ScopeId, account.BranchId) ||
             Same(x.ScopeType, "Territory") && Same(x.ScopeId, account.TerritoryId))).Select(x => x.CrmUserId).Distinct().ToArray();
        return data.Find<CrmUser>(x => assignments.Contains(x.Id)).Where(x => x.IsActiveAt(nowUtc)).OrderBy(x => x.DisplayName)
            .Select(x => new UserOptionDto(x.Id, x.DisplayName)).ToList();
    }

    public static Dictionary<Guid, string> UserNames(CrmDataSet data, IEnumerable<Guid> ids)
    {
        var set = ids.Where(x => x != Guid.Empty).Distinct().ToArray();
        return set.Length == 0 ? [] : data.Find<CrmUser>(x => set.Contains(x.Id)).ToDictionary(x => x.Id, x => x.DisplayName);
    }

    public static bool Same(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
