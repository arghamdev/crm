namespace Crm.Application.Contracts;

public sealed record PermissionOptionDto(string Key, string Module, bool IsExternalAllowed);

public sealed record RoleSummaryDto(string RoleKey, string Label, bool IsExternal, int PermissionCount, int ActiveUserCount, long Version,
    IReadOnlyList<string>? ScopeTypes = null, bool IsSystem = false);

public sealed record CreateRoleCommand(string? RoleKey, string? Label, IReadOnlyList<string>? ScopeTypes, string? CopyPermissionsFrom, string? Reason);

public sealed record RoleDetailsDto(
    RoleSummaryDto Role,
    IReadOnlySet<string> Permissions,
    IReadOnlyList<PermissionOptionDto> Catalog);

public sealed record UpdateRolePermissionsCommand(IReadOnlyList<string>? Permissions, string? Reason, long ExpectedVersion);

public sealed record RolePermissionChangeResult(IReadOnlyList<string> Added, IReadOnlyList<string> Removed, int AffectedUsers);
