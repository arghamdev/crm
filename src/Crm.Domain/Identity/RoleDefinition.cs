using Crm.Domain.Common;

namespace Crm.Domain.Identity;

/// <summary>
/// A role whose permission set is data, not code. The Version of the role guards concurrent edits of its grants.
/// </summary>
public sealed class RoleDefinition(Guid id, string roleKey, string label, bool isExternal) : Entity(id)
{
    public string RoleKey { get; } = string.IsNullOrWhiteSpace(roleKey) ? throw new ArgumentException("Role key is required.", nameof(roleKey)) : roleKey.Trim();
    public string Label { get; private set; } = string.IsNullOrWhiteSpace(label) ? throw new ArgumentException("Label is required.", nameof(label)) : label.Trim();

    /// <summary>External roles (dealer users) may only hold permissions from the external allow-list.</summary>
    public bool IsExternal { get; } = isExternal;

    private RoleDefinition() : this(Guid.Empty, "EF", "EF", false) { }

    /// <summary>Records that the role's permission set changed so concurrent editors get a version conflict.</summary>
    public void MarkPermissionsChanged() => Touch();
}

public sealed class RolePermissionGrant(Guid id, string roleKey, string permission) : Entity(id)
{
    public string RoleKey { get; } = string.IsNullOrWhiteSpace(roleKey) ? throw new ArgumentException("Role key is required.", nameof(roleKey)) : roleKey.Trim();
    public string Permission { get; } = string.IsNullOrWhiteSpace(permission) ? throw new ArgumentException("Permission is required.", nameof(permission)) : permission.Trim();

    private RolePermissionGrant() : this(Guid.Empty, "EF", "EF") { }
}
