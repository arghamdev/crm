using System.Text.Json;
using Crm.Application.Abstractions;
using Microsoft.Extensions.Caching.Distributed;

namespace Crm.Infrastructure.Identity;

public sealed class DemoAccessSnapshotService(
    ICrmDataStore store,
    IDistributedCache cache) : IAccessSnapshotService
{

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
            var user = data.Find<Crm.Domain.Identity.CrmUser>(x => x.Id == userId).SingleOrDefault(x => x.IsActiveAt(now));
            if (user is null) return null;
            var assignments = data.Find<Crm.Domain.Identity.UserRoleAssignment>(x => x.CrmUserId == userId).Where(x => x.IsEffective(now)).ToList();
            var roleKeys = assignments.Select(x => x.RoleKey).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var grantsByRole = data.Find<Crm.Domain.Identity.RolePermissionGrant>(x => roleKeys.Contains(x.RoleKey))
                .GroupBy(x => x.RoleKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => (IReadOnlySet<string>)g.Select(x => x.Permission).ToHashSet(StringComparer.OrdinalIgnoreCase),
                    StringComparer.OrdinalIgnoreCase);
            IEnumerable<string> PermissionsFor(string roleKey) =>
                grantsByRole.TryGetValue(roleKey, out var values) ? values : Array.Empty<string>();
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

    private static string Key(Guid userId) => $"crm:access:v7:{userId:N}";

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
