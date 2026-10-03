using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Domain.Identity;

namespace Crm.Application.Services;

/// <summary>
/// Edits the role → permission matrix. Role definitions are global (shared by all companies); the actor needs
/// Administration.Manage in the selected company. Guards: permissions must exist in the catalog, external roles
/// only receive external-safe permissions, and at least one active user must keep Administration.Manage.
/// </summary>
public sealed class RoleAdministrationService(ICrmDataStore store, IAccessSnapshotService access) : IRoleAdministrationService
{
    public const string AdministrationPermission = "Administration.Manage";

    /// <summary>Permissions an external (dealer) role may hold; everything else is internal data.</summary>
    private static readonly HashSet<string> ExternalAllowList = new(StringComparer.OrdinalIgnoreCase)
        { "Portal.Read", "Portal.Submit", "Dashboard.Read", "Dealer.Read", "Dealer.Portal", "Dealer.Financial.Read", "WorkQueue.Read" };

    private static readonly IReadOnlyDictionary<string, string> ModuleLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Administration"] = "مدیریت سامانه", ["Customer"] = "مشتری", ["Lead"] = "سرنخ", ["Opportunity"] = "فرصت فروش",
        ["Quote"] = "پیشنهاد قیمت", ["Order"] = "سفارش", ["Dealer"] = "نمایندگان", ["Portal"] = "پرتال نماینده",
        ["Mobile"] = "موبایل", ["Reporting"] = "گزارش", ["Dashboard"] = "داشبورد", ["WorkQueue"] = "کارتابل", ["Service"] = "خدمات و SLA"
    };

    /// <summary>Every permission the application checks; seeded from the shipped matrix so names cannot be mistyped in the UI.</summary>
    public static IReadOnlyList<PermissionOptionDto> Catalog { get; } = DefaultRolePermissions.Permissions.Values
        .SelectMany(x => x).Distinct(StringComparer.OrdinalIgnoreCase)
        .Select(x => new PermissionOptionDto(x, Module(x), ExternalAllowList.Contains(x)))
        .OrderBy(x => x.Module, StringComparer.Ordinal).ThenBy(x => x.Key, StringComparer.Ordinal).ToList();

    public IReadOnlyList<RoleSummaryDto> GetRoles(Guid actorUserId, string companyId, DateTimeOffset nowUtc)
    {
        RequireAdministrator(actorUserId, companyId);
        return store.Read(data => data.RoleDefinitions.OrderBy(x => x.IsExternal).ThenBy(x => x.Label)
            .Select(x => Summary(data, x, nowUtc)).ToList());
    }

    public RoleDetailsDto? GetRole(Guid actorUserId, string companyId, string roleKey, DateTimeOffset nowUtc)
    {
        RequireAdministrator(actorUserId, companyId);
        return store.Read(data =>
        {
            var role = data.RoleDefinitions.SingleOrDefault(x => Same(x.RoleKey, roleKey));
            if (role is null) return null;
            var granted = data.RolePermissionGrants.Where(x => Same(x.RoleKey, role.RoleKey)).Select(x => x.Permission)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return new RoleDetailsDto(Summary(data, role, nowUtc), granted,
                role.IsExternal ? Catalog.Where(x => x.IsExternalAllowed).ToList() : Catalog);
        });
    }

    /// <summary>
    /// Creates an internal role. Keys are stable identifiers (letters/digits, starting with a letter); permissions can be
    /// copied from an existing role as a starting point and are then edited like any other role. Custom roles never
    /// receive the hard-coded manager-wide visibility that SalesManager/SalesSupervisor keys carry.
    /// </summary>
    public RoleSummaryDto CreateRole(Guid actorUserId, string companyId, CreateRoleCommand command, IdentityRequestContext context)
    {
        RequireAdministrator(actorUserId, companyId);
        var key = command.RoleKey?.Trim() ?? string.Empty;
        if (!System.Text.RegularExpressions.Regex.IsMatch(key, "^[A-Za-z][A-Za-z0-9]{2,39}$"))
            throw new InvalidOperationException("کلید نقش باید ۳ تا ۴۰ حرف و رقم لاتین و با حرف شروع شود.");
        if (string.IsNullOrWhiteSpace(command.Label)) throw new InvalidOperationException("عنوان نقش الزامی است.");
        if (string.IsNullOrWhiteSpace(command.Reason)) throw new InvalidOperationException("دلیل ایجاد نقش الزامی است.");
        var scopes = (command.ScopeTypes ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (scopes.Count == 0 || scopes.Any(x => !RoleDefinition.InternalScopeTypes.Contains(x, StringComparer.OrdinalIgnoreCase)))
            throw new InvalidOperationException("حداقل یک دامنهٔ معتبر (شرکت، شعبه یا قلمرو) انتخاب کنید.");

        return store.Write(data =>
        {
            if (data.RoleDefinitions.Any(x => x.RoleKey.Equals(key, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("نقشی با این کلید وجود دارد.");
            var role = new RoleDefinition(Guid.NewGuid(), key, command.Label!, isExternal: false, string.Join(",", scopes));
            data.RoleDefinitions.Add(role);
            var copied = new List<string>();
            if (!string.IsNullOrWhiteSpace(command.CopyPermissionsFrom))
            {
                var source = data.RoleDefinitions.SingleOrDefault(x => Same(x.RoleKey, command.CopyPermissionsFrom)) ??
                    throw new InvalidOperationException("نقش مبدأ برای کپی مجوزها پیدا نشد.");
                if (source.IsExternal) throw new InvalidOperationException("مجوزهای نقش بیرونی قابل کپی در نقش داخلی نیست.");
                copied = data.RolePermissionGrants.Where(x => Same(x.RoleKey, source.RoleKey)).Select(x => x.Permission).ToList();
                foreach (var permission in copied) data.RolePermissionGrants.Add(new RolePermissionGrant(Guid.NewGuid(), role.RoleKey, permission));
            }
            data.Append(new SecurityAuditEvent(Guid.NewGuid(), context.NowUtc, "RoleCreated", "Success", actorUserId, null, null,
                context.CorrelationId, Truncate($"{role.RoleKey} [{role.AllowedScopeTypes}] copied {copied.Count} from {command.CopyPermissionsFrom ?? "-"}; {command.Reason!.Trim()}", 1000),
                context.IpHash, context.UserAgentSummary));
            return Summary(data, role, context.NowUtc);
        });
    }

    public RolePermissionChangeResult UpdatePermissions(Guid actorUserId, string companyId, string roleKey,
        UpdateRolePermissionsCommand command, IdentityRequestContext context)
    {
        RequireAdministrator(actorUserId, companyId);
        if (string.IsNullOrWhiteSpace(command.Reason)) throw new InvalidOperationException("دلیل تغییر مجوزها الزامی است.");
        var requested = (command.Permissions ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknown = requested.Where(x => !Catalog.Any(c => Same(c.Key, x))).ToList();
        if (unknown.Count > 0) throw new InvalidOperationException("مجوز ناشناخته: " + string.Join("، ", unknown));

        var (result, affectedUsers) = store.Write(data =>
        {
            var role = data.RoleDefinitions.SingleOrDefault(x => Same(x.RoleKey, roleKey)) ??
                throw new KeyNotFoundException("نقش پیدا نشد.");
            if (role.Version != command.ExpectedVersion)
                throw new InvalidOperationException("مجوزهای این نقش هم‌زمان تغییر کرده است؛ صفحه را تازه‌سازی کنید.");
            if (role.IsExternal)
            {
                var forbidden = requested.Where(x => !ExternalAllowList.Contains(x)).ToList();
                if (forbidden.Count > 0)
                    throw new InvalidOperationException("نقش بیرونی (نماینده) نمی‌تواند مجوز داخلی بگیرد: " + string.Join("، ", forbidden));
            }

            var current = data.RolePermissionGrants.Where(x => Same(x.RoleKey, role.RoleKey)).ToList();
            var added = requested.Where(x => !current.Any(g => Same(g.Permission, x))).Order(StringComparer.Ordinal).ToList();
            var removed = current.Where(g => !requested.Contains(g.Permission)).ToList();
            if (added.Count == 0 && removed.Count == 0)
                return (new RolePermissionChangeResult([], [], 0), new List<Guid>());

            if (removed.Any(x => Same(x.Permission, AdministrationPermission)) &&
                !AdministratorRemains(data, role.RoleKey, context.NowUtc))
                throw new InvalidOperationException("حذف این مجوز هیچ کاربر فعالی با دسترسی مدیریت سامانه باقی نمی‌گذارد.");

            foreach (var grant in removed) data.RolePermissionGrants.Remove(grant);
            foreach (var permission in added) data.RolePermissionGrants.Add(new RolePermissionGrant(Guid.NewGuid(), role.RoleKey, permission));
            role.MarkPermissionsChanged();

            var users = data.UserRoleAssignments.Where(x => Same(x.RoleKey, role.RoleKey) && x.IsEffective(context.NowUtc))
                .Select(x => x.CrmUserId).Distinct().ToList();
            data.Append<Crm.Domain.Identity.SecurityAuditEvent>(new SecurityAuditEvent(Guid.NewGuid(), context.NowUtc, "RolePermissionsChanged", "Success",
                actorUserId, null, null, context.CorrelationId,
                Truncate($"{role.RoleKey}: +[{string.Join(",", added)}] -[{string.Join(",", removed.Select(x => x.Permission))}]; {command.Reason!.Trim()}", 1000),
                context.IpHash, context.UserAgentSummary));
            return (new RolePermissionChangeResult(added, removed.Select(x => x.Permission).Order(StringComparer.Ordinal).ToList(), users.Count), users);
        });
        // Cached snapshots would otherwise keep the old permissions for up to five minutes.
        foreach (var userId in affectedUsers) access.Invalidate(userId);
        return result;
    }

    private static bool AdministratorRemains(CrmDataSet data, string roleLosingPermission, DateTimeOffset nowUtc)
    {
        var adminRoles = data.RolePermissionGrants.Where(x => Same(x.Permission, AdministrationPermission) && !Same(x.RoleKey, roleLosingPermission))
            .Select(x => x.RoleKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var activeUsers = data.Users.Where(x => x.IsActiveAt(nowUtc)).Select(x => x.Id).ToHashSet();
        return data.UserRoleAssignments.Any(x => adminRoles.Contains(x.RoleKey) && x.IsEffective(nowUtc) && activeUsers.Contains(x.CrmUserId));
    }

    private static RoleSummaryDto Summary(CrmDataSet data, RoleDefinition role, DateTimeOffset nowUtc) => new(
        role.RoleKey, role.Label, role.IsExternal,
        data.RolePermissionGrants.Count(x => Same(x.RoleKey, role.RoleKey)),
        data.UserRoleAssignments.Where(x => Same(x.RoleKey, role.RoleKey) && x.IsEffective(nowUtc)).Select(x => x.CrmUserId).Distinct().Count(),
        role.Version, role.ScopeTypes, role.IsSystem);

    private void RequireAdministrator(Guid actorUserId, string companyId)
    {
        if (!access.HasPermission(actorUserId, companyId, AdministrationPermission))
            throw new UnauthorizedAccessException("مدیریت نقش‌ها به مجوز مدیریت سامانه نیاز دارد.");
    }

    private static string Module(string permission)
    {
        var prefix = permission.Split('.')[0];
        return ModuleLabels.TryGetValue(prefix, out var label) ? label : prefix;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..(max - 1)] + "…";

    private static bool Same(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
