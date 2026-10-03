using System.Text.Json;
using Crm.Application.Abstractions;
using Microsoft.Extensions.Caching.Distributed;

namespace Crm.Infrastructure.Identity;

public sealed class DemoAccessSnapshotService(
    ICrmDataStore store,
    IDistributedCache cache) : IAccessSnapshotService
{
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> RolePermissions =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["CompanyMember"] = Set(),
            ["SalesManager"] = Set(
                "Portal.Review", "Mobile.Visit.Read", "Mobile.Visit.Write",
                "Reporting.Read", "Reporting.Export", "Reporting.BiExport",
                "Dashboard.Read", "Customer.Read", "Customer.Create", "Customer.Update", "Customer.MergeReview",
                "Customer.Contact.Read", "Customer.NationalId.Read", "Customer.Financial.Read", "Quote.Margin.Read",
                "Lead.Read", "Lead.Create", "Lead.Assign", "Lead.Update", "Lead.Convert",
                "Opportunity.Read", "Opportunity.Create", "Opportunity.Update", "Opportunity.Assign", "Opportunity.Close", "Opportunity.Advance",
                "Quote.Read", "Quote.Create", "Quote.Update", "Quote.Submit", "Quote.Approve",
                "Quote.Approve.Supervisor", "Quote.Approve.Commercial", "Quote.Approve.Sales",
                "Quote.Send", "Quote.Accept", "Quote.Expire",
                "Order.Read", "Order.Create", "Order.CreditCheck", "Order.Submit", "Order.Integration.Process", "Order.Sync",
                "Dealer.Read", "Dealer.Contract.Request", "Dealer.Territory.Request", "Dealer.Customer.Assign",
                "Service.Read", "Service.Create", "Service.Update", "Service.Triage",
                "WorkQueue.Read", "WorkQueue.Complete", "Administration.Manage"),
            ["SalesSupervisor"] = Set(
                "Mobile.Visit.Read", "Mobile.Visit.Write",
                "Reporting.Read", "Reporting.Export",
                "Dashboard.Read", "Customer.Read", "Customer.Create", "Customer.Update",
                "Customer.Contact.Read", "Customer.NationalId.Read", "Customer.Financial.Read", "Quote.Margin.Read",
                "Lead.Read", "Lead.Create", "Lead.Assign", "Lead.Update", "Lead.Convert",
                "Opportunity.Read", "Opportunity.Create", "Opportunity.Update", "Opportunity.Assign", "Opportunity.Close", "Opportunity.Advance",
                "Quote.Read", "Quote.Create", "Quote.Update", "Quote.Submit", "Quote.Approve",
                "Quote.Approve.Supervisor", "Quote.Send", "Quote.Accept", "Quote.Expire",
                "Order.Read", "Order.Create", "Order.CreditCheck", "Order.Submit",
                "Service.Read", "Service.Create", "Service.Update", "Service.Triage",
                "WorkQueue.Read", "WorkQueue.Complete"),
            ["SalesExpert"] = Set(
                "Mobile.Visit.Read", "Mobile.Visit.Write",
                "Reporting.Read",
                "Dashboard.Read", "Customer.Read", "Customer.Create", "Customer.Contact.Read",
                "Lead.Read", "Lead.Create", "Lead.Update", "Lead.Convert",
                "Opportunity.Read", "Opportunity.Create", "Opportunity.Update", "Opportunity.Advance",
                "Quote.Read", "Quote.Create", "Quote.Update", "Quote.Submit", "Quote.Send",
                "Quote.Accept", "Quote.Expire", "Order.Read", "Order.Create", "Order.CreditCheck", "Order.Submit",
                "Service.Read", "Service.Create", "Service.Update",
                "WorkQueue.Read", "WorkQueue.Complete"),
            ["FinanceManager"] = Set(
                "Reporting.Read", "Reporting.Export", "Reporting.BiExport", "Reporting.Financial.Read",
                "Dashboard.Read", "Customer.Read", "Opportunity.Read", "Quote.Read",
                "Customer.Contact.Read", "Customer.NationalId.Read", "Customer.Financial.Read", "Quote.Margin.Read",
                "Quote.Approve.Finance", "Order.Read", "Order.CreditOverride",
                "Dealer.Read", "Dealer.Financial.Read", "WorkQueue.Read", "WorkQueue.Complete"),
            ["ChannelManager"] = Set(
                "Portal.Review",
                "Reporting.Read", "Reporting.Export",
                "Dashboard.Read", "Customer.Read", "Dealer.Read", "Dealer.Manage", "Dealer.Submit", "Dealer.Approve",
                "Dealer.Contract.Request", "Dealer.Contract.Approve", "Dealer.Territory.Request", "Dealer.Territory.Approve",
                "Dealer.Customer.Assign", "Dealer.Target.Manage", "Dealer.Financial.Read", "Dealer.Financial.Sync",
                "Dealer.Performance.Sync", "WorkQueue.Read", "WorkQueue.Complete"),
            ["Executive"] = Set("Dashboard.Read", "Reporting.Read", "Reporting.Export", "Reporting.BiExport", "Reporting.Financial.Read",
                "Customer.Read", "Lead.Read", "Opportunity.Read", "Quote.Read", "Order.Read", "Dealer.Read", "Dealer.Financial.Read",
                "Customer.Financial.Read", "Quote.Margin.Read",
                "Service.Read", "Service.ReadAll", "WorkQueue.Read"),
            ["ServiceAgent"] = Set(
                "Dashboard.Read", "Customer.Read", "Customer.Contact.Read", "Service.Read", "Service.Create", "Service.Update",
                "WorkQueue.Read", "WorkQueue.Complete"),
            ["DealerUser"] = Set(
                "Portal.Read", "Portal.Submit",
                "Dashboard.Read", "Dealer.Read", "Dealer.Portal", "Dealer.Financial.Read", "WorkQueue.Read")
        };

    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public AccessSnapshot? Get(Guid userId)
    {
        var now = DateTimeOffset.UtcNow;
        var key = Key(userId);
        var serialized = cache.GetString(key);
        if (!string.IsNullOrWhiteSpace(serialized))
        {
            var payload = JsonSerializer.Deserialize<CachedSnapshot>(serialized, JsonOptions);
            if (payload is not null && payload.ExpiresAtUtc > now) return payload.ToDomain();
        }

        var snapshot = store.Read(data =>
        {
            var user = data.Users.SingleOrDefault(x => x.Id == userId && x.IsActiveAt(now));
            if (user is null) return null;
            var assignments = data.UserRoleAssignments.Where(x => x.CrmUserId == userId && x.IsEffective(now)).ToList();
            var permissions = assignments.SelectMany(x => PermissionsFor(x.RoleKey))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var roles = assignments.Select(x => x.RoleLabel).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var scopes = assignments.Select(x => new ScopeGrant(x.CompanyId, x.RoleKey, x.ScopeType, x.ScopeId, x.ScopeLabel)).ToList();
            var permissionScopes = assignments.SelectMany(assignment => PermissionsFor(assignment.RoleKey)
                .Select(permission => new PermissionScopeGrant(assignment.CompanyId, permission, assignment.ScopeType, assignment.ScopeId)))
                .ToList();
            var companyPermissionSets = assignments.GroupBy(x => x.CompanyId, StringComparer.OrdinalIgnoreCase)
                .Select(group => new CompanyPermissionSet(
                    group.Key,
                    group.SelectMany(x => PermissionsFor(x.RoleKey)).ToHashSet(StringComparer.OrdinalIgnoreCase),
                    group.Select(x => x.RoleLabel).ToHashSet(StringComparer.OrdinalIgnoreCase)))
                .ToList();
            return new AccessSnapshot(user.Id, user.SecurityVersion, permissions, roles, scopes, permissionScopes,
                companyPermissionSets, now, now.Add(CacheLifetime));
        });
        if (snapshot is not null)
        {
            var payload = CachedSnapshot.FromDomain(snapshot);
            cache.SetString(key, JsonSerializer.Serialize(payload, JsonOptions), new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = CacheLifetime
            });
        }
        return snapshot;
    }

    public bool HasPermission(Guid userId, string companyId, string permission) =>
        Get(userId)?.PermissionsFor(companyId).Contains(permission) == true;

    public void Invalidate(Guid userId) => cache.Remove(Key(userId));

    private static string Key(Guid userId) => $"crm:access:v4:{userId:N}";
    private static IReadOnlySet<string> Set(params string[] values) => new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);
    private static IEnumerable<string> PermissionsFor(string roleKey) =>
        RolePermissions.TryGetValue(roleKey, out var values) ? values : Array.Empty<string>();

    private sealed record CachedCompanyPermissionSet(string CompanyId, string[] Permissions, string[] RoleLabels);

    private sealed record CachedSnapshot(
        Guid UserId,
        long SecurityVersion,
        string[] Permissions,
        string[] RoleLabels,
        ScopeGrant[] ScopeGrants,
        PermissionScopeGrant[] PermissionScopeGrants,
        CachedCompanyPermissionSet[] CompanyPermissionSets,
        DateTimeOffset GeneratedAtUtc,
        DateTimeOffset ExpiresAtUtc)
    {
        public static CachedSnapshot FromDomain(AccessSnapshot value) => new(
            value.UserId,
            value.SecurityVersion,
            value.Permissions.ToArray(),
            value.RoleLabels.ToArray(),
            value.ScopeGrants.ToArray(),
            value.PermissionScopeGrants.ToArray(),
            value.CompanyPermissionSets.Select(x => new CachedCompanyPermissionSet(
                x.CompanyId, x.Permissions.ToArray(), x.RoleLabels.ToArray())).ToArray(),
            value.GeneratedAtUtc,
            value.ExpiresAtUtc);

        public AccessSnapshot ToDomain() => new(
            UserId,
            SecurityVersion,
            Permissions.ToHashSet(StringComparer.OrdinalIgnoreCase),
            RoleLabels.ToHashSet(StringComparer.OrdinalIgnoreCase),
            ScopeGrants,
            PermissionScopeGrants,
            CompanyPermissionSets.Select(x => new CompanyPermissionSet(
                x.CompanyId,
                x.Permissions.ToHashSet(StringComparer.OrdinalIgnoreCase),
                x.RoleLabels.ToHashSet(StringComparer.OrdinalIgnoreCase))).ToArray(),
            GeneratedAtUtc,
            ExpiresAtUtc);
    }
}
