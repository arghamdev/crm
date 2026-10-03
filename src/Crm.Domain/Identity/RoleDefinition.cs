using Crm.Domain.Common;

namespace Crm.Domain.Identity;

/// <summary>
/// A role whose permission set is data, not code. The Version of the role guards concurrent edits of its grants.
/// </summary>
public sealed class RoleDefinition(Guid id, string roleKey, string label, bool isExternal, string allowedScopeTypes = "Company",
    bool isSystem = false) : Entity(id)
{
    public static readonly string[] InternalScopeTypes = ["Company", "Branch", "Territory"];

    public string RoleKey { get; } = string.IsNullOrWhiteSpace(roleKey) ? throw new ArgumentException("Role key is required.", nameof(roleKey)) : roleKey.Trim();
    public string Label { get; private set; } = string.IsNullOrWhiteSpace(label) ? throw new ArgumentException("Label is required.", nameof(label)) : label.Trim();

    /// <summary>External roles (dealer users) may only hold permissions from the external allow-list.</summary>
    public bool IsExternal { get; } = isExternal;

    /// <summary>Comma-separated scope types a user may be assigned this role on (Company, Branch, Territory; Dealer for external roles).</summary>
    public string AllowedScopeTypes { get; private set; } = NormalizeScopes(allowedScopeTypes);

    /// <summary>Shipped roles: code relies on their keys (e.g. manager-wide visibility), so they cannot be renamed or removed.</summary>
    public bool IsSystem { get; } = isSystem;

    public IReadOnlyList<string> ScopeTypes => AllowedScopeTypes.Split(',', StringSplitOptions.RemoveEmptyEntries);

    public bool AllowsScope(string scopeType) => ScopeTypes.Contains(scopeType, StringComparer.OrdinalIgnoreCase);

    private RoleDefinition() : this(Guid.Empty, "EF", "EF", false) { }

    private static string NormalizeScopes(string value)
    {
        var scopes = (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (scopes.Length == 0) throw new ArgumentException("At least one scope type is required.", nameof(value));
        return string.Join(",", scopes);
    }

    /// <summary>Records that the role's permission set changed so concurrent editors get a version conflict.</summary>
    public void MarkPermissionsChanged() => Touch();
}

public sealed class RolePermissionGrant(Guid id, string roleKey, string permission) : Entity(id)
{
    public string RoleKey { get; } = string.IsNullOrWhiteSpace(roleKey) ? throw new ArgumentException("Role key is required.", nameof(roleKey)) : roleKey.Trim();
    public string Permission { get; } = string.IsNullOrWhiteSpace(permission) ? throw new ArgumentException("Permission is required.", nameof(permission)) : permission.Trim();

    private RolePermissionGrant() : this(Guid.Empty, "EF", "EF") { }
}
