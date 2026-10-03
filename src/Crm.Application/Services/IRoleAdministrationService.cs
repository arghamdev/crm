using Crm.Application.Contracts;

namespace Crm.Application.Services;

public interface IRoleAdministrationService
{
    IReadOnlyList<RoleSummaryDto> GetRoles(Guid actorUserId, string companyId, DateTimeOffset nowUtc);
    RoleDetailsDto? GetRole(Guid actorUserId, string companyId, string roleKey, DateTimeOffset nowUtc);
    RolePermissionChangeResult UpdatePermissions(Guid actorUserId, string companyId, string roleKey,
        UpdateRolePermissionsCommand command, IdentityRequestContext context);
}
