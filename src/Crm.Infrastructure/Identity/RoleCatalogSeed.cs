using System.Security.Cryptography;
using System.Text;
using Crm.Application.Services;
using Crm.Domain.Identity;

namespace Crm.Infrastructure.Identity;

/// <summary>
/// Seed rows for the role catalog. Ids are derived from the role key / permission so the in-memory sample,
/// the SQL seed and the migration all describe the same rows.
/// </summary>
public static class RoleCatalogSeed
{
    public static IEnumerable<RoleDefinition> Roles() =>
        DefaultRolePermissions.Roles.Select(x => new RoleDefinition(Id("role:" + x.Key), x.Key, x.Value.Label, x.Value.IsExternal,
            x.Value.ScopeTypes, isSystem: true));

    public static IEnumerable<RolePermissionGrant> Grants() =>
        DefaultRolePermissions.Permissions.SelectMany(role => role.Value.Order(StringComparer.Ordinal)
            .Select(permission => new RolePermissionGrant(Id("grant:" + role.Key + ":" + permission), role.Key, permission)));

    public static Guid Id(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.ToUpperInvariant()))[..16];
        bytes[7] = (byte)(bytes[7] & 0x0F | 0x50);
        bytes[8] = (byte)(bytes[8] & 0x3F | 0x80);
        return new Guid(bytes);
    }
}
