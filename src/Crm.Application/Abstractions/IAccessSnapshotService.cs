namespace Crm.Application.Abstractions;

public sealed record ScopeGrant(string CompanyId, string RoleKey, string Type, string Id, string Label);
public sealed record PermissionScopeGrant(string CompanyId, string Permission, string ScopeType, string ScopeId);

public sealed record CompanyPermissionSet(
    string CompanyId,
    IReadOnlySet<string> Permissions,
    IReadOnlySet<string> RoleLabels);

public sealed record AccessSnapshot(
    Guid UserId,
    long SecurityVersion,
    IReadOnlySet<string> Permissions,
    IReadOnlySet<string> RoleLabels,
    IReadOnlyList<ScopeGrant> ScopeGrants,
    IReadOnlyList<PermissionScopeGrant> PermissionScopeGrants,
    IReadOnlyList<CompanyPermissionSet> CompanyPermissionSets,
    DateTimeOffset GeneratedAtUtc,
    DateTimeOffset ExpiresAtUtc)
{
    public IReadOnlySet<string> PermissionsFor(string companyId) => CompanyPermissionSets
        .FirstOrDefault(x => x.CompanyId.Equals(companyId, StringComparison.OrdinalIgnoreCase))?.Permissions ??
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> RoleLabelsFor(string companyId) => CompanyPermissionSets
        .FirstOrDefault(x => x.CompanyId.Equals(companyId, StringComparison.OrdinalIgnoreCase))?.RoleLabels ??
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public bool HasCompany(string companyId) => ScopeGrants.Any(x =>
        x.CompanyId.Equals(companyId, StringComparison.OrdinalIgnoreCase));

    public bool Covers(string companyId, string scopeType, string scopeId)
    {
        var companyScopes = ScopeGrants.Where(x => x.CompanyId.Equals(companyId, StringComparison.OrdinalIgnoreCase)).ToList();
        var exact = companyScopes.Any(x => x.Type.Equals(scopeType, StringComparison.OrdinalIgnoreCase) &&
            x.Id.Equals(scopeId, StringComparison.OrdinalIgnoreCase));
        if (scopeType.Equals("Company", StringComparison.OrdinalIgnoreCase)) return HasCompany(companyId);
        if (!scopeType.Equals("Branch", StringComparison.OrdinalIgnoreCase) &&
            !scopeType.Equals("Territory", StringComparison.OrdinalIgnoreCase)) return exact;
        var hasRestriction = companyScopes.Any(x => x.Type.Equals(scopeType, StringComparison.OrdinalIgnoreCase));
        return hasRestriction ? exact : companyScopes.Any(x => x.Type.Equals("Company", StringComparison.OrdinalIgnoreCase));
    }

    public bool Allows(string companyId, string permission, string scopeType, string scopeId)
    {
        var grants = PermissionScopeGrants.Where(x =>
            x.CompanyId.Equals(companyId, StringComparison.OrdinalIgnoreCase) &&
            x.Permission.Equals(permission, StringComparison.OrdinalIgnoreCase)).ToList();
        if (grants.Count == 0) return false;
        if (scopeType.Equals("Company", StringComparison.OrdinalIgnoreCase)) return true;
        return grants.Any(x => x.ScopeType.Equals("Company", StringComparison.OrdinalIgnoreCase) ||
            x.ScopeType.Equals(scopeType, StringComparison.OrdinalIgnoreCase) &&
            x.ScopeId.Equals(scopeId, StringComparison.OrdinalIgnoreCase));
    }

    public bool AllowsRecord(string companyId, string permission, string branchId, string? territoryId)
    {
        var grants = PermissionScopeGrants.Where(x =>
            x.CompanyId.Equals(companyId, StringComparison.OrdinalIgnoreCase) &&
            x.Permission.Equals(permission, StringComparison.OrdinalIgnoreCase));
        return grants.Any(x =>
            x.ScopeType.Equals("Company", StringComparison.OrdinalIgnoreCase) ||
            x.ScopeType.Equals("Branch", StringComparison.OrdinalIgnoreCase) &&
            x.ScopeId.Equals(branchId, StringComparison.OrdinalIgnoreCase) ||
            territoryId is not null && x.ScopeType.Equals("Territory", StringComparison.OrdinalIgnoreCase) &&
            x.ScopeId.Equals(territoryId, StringComparison.OrdinalIgnoreCase));
    }
}

public interface IAccessSnapshotService
{
    AccessSnapshot? Get(Guid userId);
    bool HasPermission(Guid userId, string companyId, string permission);
    void Invalidate(Guid userId);
}
