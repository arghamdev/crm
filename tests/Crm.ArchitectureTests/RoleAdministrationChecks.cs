using Crm.Application.Contracts;
using Crm.Application.Services;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Identity;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

internal static class RoleAdministrationChecks
{
    internal static void Run(Action<bool, string> check)
    {
        using var store = new InMemoryCrmDataStore();
        using var provider = new ServiceCollection().AddDistributedMemoryCache().BuildServiceProvider();
        var access = new DemoAccessSnapshotService(store, provider.GetRequiredService<IDistributedCache>());
        var roles = new RoleAdministrationService(store, access);
        Guid User(int n) => Guid.Parse($"10000000-0000-4000-8000-{n:000000000000}");
        var manager = User(1); var expert = User(2);
        var now = DateTimeOffset.UtcNow;
        IdentityRequestContext Context() => new(now, "ip", "tests", Guid.NewGuid().ToString("N"));
        void Reject<T>(Action action, string name) where T : Exception
        {
            try { action(); check(false, "ROLES: " + name + " must be rejected."); } catch (T) { }
        }

        check(store.Read(d => d.RoleDefinitions.Count == DefaultRolePermissions.Roles.Count &&
                d.RolePermissionGrants.Count == DefaultRolePermissions.Permissions.Sum(x => x.Value.Count)),
            "ROLES: the seed reproduces the shipped role matrix exactly.");
        check(RoleAdministrationService.Catalog.Count > 50 && RoleAdministrationService.Catalog.All(x => x.Module.Length > 0),
            "ROLES: the permission catalog lists every known permission with a module.");
        Reject<UnauthorizedAccessException>(() => roles.GetRoles(expert, "C01", now), "role list without Administration.Manage");

        var list = roles.GetRoles(manager, "C01", now);
        var expertRole = list.Single(x => x.RoleKey == "SalesExpert");
        check(expertRole.ActiveUserCount >= 1 && !expertRole.IsExternal, "ROLES: the list shows the user count per role.");

        // Removing a permission takes effect on the very next request, without waiting for the 5-minute snapshot cache.
        check(access.HasPermission(expert, "C01", "Lead.Create"), "ROLES: precondition — the expert can create leads.");
        var details = roles.GetRole(manager, "C01", "SalesExpert", now)!;
        var without = details.Permissions.Where(x => x != "Lead.Create").ToList();
        var change = roles.UpdatePermissions(manager, "C01", "SalesExpert", new UpdateRolePermissionsCommand(without, "آزمون", details.Role.Version), Context());
        check(change.Removed.SequenceEqual(["Lead.Create"]) && change.Added.Count == 0 && change.AffectedUsers >= 1,
            "ROLES: the change result reports exactly what changed.");
        check(!access.HasPermission(expert, "C01", "Lead.Create"), "ROLES: the removed permission is revoked immediately for affected users.");
        check(store.Read(d => d.SecurityAuditEvents.Any(x => x.EventType == "RolePermissionsChanged" && x.Reason.Contains("-[Lead.Create]"))),
            "ROLES: permission changes are audited with the diff.");

        Reject<InvalidOperationException>(() => roles.UpdatePermissions(manager, "C01", "SalesExpert",
            new UpdateRolePermissionsCommand(details.Permissions.ToList(), "stale", details.Role.Version), Context()), "a stale version");
        var current = roles.GetRole(manager, "C01", "SalesExpert", now)!;
        Reject<InvalidOperationException>(() => roles.UpdatePermissions(manager, "C01", "SalesExpert",
            new UpdateRolePermissionsCommand([.. current.Permissions, "Lead.Delete.Everything"], "x", current.Role.Version), Context()), "an unknown permission");
        Reject<InvalidOperationException>(() => roles.UpdatePermissions(manager, "C01", "SalesExpert",
            new UpdateRolePermissionsCommand(current.Permissions.ToList(), " ", current.Role.Version), Context()), "a change without a reason");
        var dealer = roles.GetRole(manager, "C01", "DealerUser", now)!;
        check(dealer.Catalog.All(x => x.IsExternalAllowed), "ROLES: an external role is only offered external-safe permissions.");
        Reject<InvalidOperationException>(() => roles.UpdatePermissions(manager, "C01", "DealerUser",
            new UpdateRolePermissionsCommand([.. dealer.Permissions, "Customer.Read"], "x", dealer.Role.Version), Context()), "an internal permission on the dealer role");

        var managerRole = roles.GetRole(manager, "C01", "SalesManager", now)!;
        Reject<InvalidOperationException>(() => roles.UpdatePermissions(manager, "C01", "SalesManager",
            new UpdateRolePermissionsCommand(managerRole.Permissions.Where(x => x != "Administration.Manage").ToList(), "x", managerRole.Role.Version), Context()),
            "removing the last Administration.Manage holder");
        check(access.HasPermission(manager, "C01", "Administration.Manage"), "ROLES: the administrator keeps access after the rejected change.");

        roles.UpdatePermissions(manager, "C01", "SalesExpert",
            new UpdateRolePermissionsCommand([.. current.Permissions, "Lead.Create"], "بازگردانی", current.Role.Version), Context());
        check(access.HasPermission(expert, "C01", "Lead.Create"), "ROLES: re-granting restores access immediately.");
    }
}
