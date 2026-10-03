using Crm.Application.Contracts;
using Crm.Domain.Identity;

namespace Crm.Application.Services;

public interface IIdentityApplicationService
{
    IReadOnlyList<UserDto> GetUsers();
    UserDetailsDto? GetUser(Guid id);
    Task<UserDetailsDto?> GetUserAsync(Guid id, CancellationToken cancellationToken = default);
    UserDto CreatePendingUser(CreatePendingUserCommand command, Guid actorUserId, IdentityRequestContext context);
    UserDto ChangeStatus(Guid id, UserStatus status, long expectedSecurityVersion, Guid actorUserId, IdentityRequestContext context);
    RoleAssignmentDto AssignRole(Guid id, AssignRoleCommand command, Guid actorUserId, IdentityRequestContext context);
    void RevokeRole(Guid userId, Guid assignmentId, long expectedSecurityVersion, Guid actorUserId, IdentityRequestContext context);
    void RevokeSession(Guid userId, Guid sessionId, Guid actorUserId, IdentityRequestContext context);
    int RevokeOtherSessions(Guid userId, Guid currentSessionId, IdentityRequestContext context);
    SignInResult AuthenticateDemo(string userName, string password, IdentityRequestContext context);
    SignInResult AuthenticateExternal(ExternalIdentityDescriptor identity, IdentityRequestContext context);
    SessionValidationResult ValidateSession(Guid sessionId, Guid userId, long securityVersion, IdentityRequestContext context);
    void RecordAccessDenied(Guid userId, string permission, IdentityRequestContext context);
    void RecordAuthenticationFailure(string reasonCode, IdentityRequestContext context);
}
