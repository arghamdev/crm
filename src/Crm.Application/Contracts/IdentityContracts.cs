using Crm.Domain.Identity;

namespace Crm.Application.Contracts;

public sealed record UserDto(
    Guid Id,
    string DisplayName,
    string UserName,
    string Email,
    string? EmployeeNumber,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Scopes,
    UserStatus Status,
    long SecurityVersion,
    bool HasExternalIdentity,
    int ActiveSessionCount,
    DateTimeOffset? LastLoginAtUtc);

public sealed record UserSessionDto(
    Guid Id,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset LastSeenAtUtc,
    DateTimeOffset IdleExpiresAtUtc,
    DateTimeOffset AbsoluteExpiresAtUtc,
    DateTimeOffset? RevokedAtUtc,
    string? RevokeReason,
    string IpHash,
    string UserAgentSummary,
    string? SelectedCompanyId,
    string? SelectedBranchId,
    string? SelectedTerritoryId);

public sealed record RoleAssignmentDto(
    Guid Id,
    string RoleKey,
    string RoleLabel,
    string CompanyId,
    string ScopeType,
    string ScopeId,
    string ScopeLabel,
    DateTimeOffset ValidFromUtc,
    DateTimeOffset? ValidToUtc,
    RoleAssignmentStatus Status,
    string Reason);

public sealed record ExternalIdentityDto(
    Guid Id,
    string ProviderKey,
    string Issuer,
    string Subject,
    string? EmailAtLink,
    DateTimeOffset LinkedAtUtc,
    DateTimeOffset LastSeenAtUtc,
    bool IsActive);

public sealed record SecurityAuditEventDto(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    string EventType,
    string Outcome,
    Guid? ActorUserId,
    Guid? TargetUserId,
    Guid? SessionId,
    string CorrelationId,
    string Reason);

public sealed record UserDetailsDto(
    UserDto User,
    IReadOnlyList<RoleAssignmentDto> RoleAssignments,
    IReadOnlyList<UserSessionDto> Sessions,
    IReadOnlyList<ExternalIdentityDto> ExternalIdentities,
    IReadOnlyList<SecurityAuditEventDto> AuditEvents);

public sealed record AssignableRoleDto(string RoleKey, string Label, IReadOnlyList<string> ScopeTypes);

public sealed record SecurityAuditQuery(string? EventType = null, string? Outcome = null, Guid? UserId = null,
    DateTimeOffset? FromUtc = null, DateTimeOffset? ToUtc = null);

public sealed record SecurityAuditPageDto(PagedResult<SecurityAuditEventDto> Events, SecurityAuditQuery Query, IReadOnlyList<string> EventTypes);

public sealed record CreatePendingUserCommand(string DisplayName, string UserName, string Email, string? EmployeeNumber);
public sealed record AssignRoleCommand(string RoleKey, string ScopeType, string ScopeId, string Reason, DateTimeOffset? ValidToUtc, long ExpectedSecurityVersion);
public sealed record IdentityRequestContext(DateTimeOffset NowUtc, string IpHash, string UserAgentSummary, string CorrelationId);

public sealed record ExternalIdentityDescriptor(
    string ProviderKey,
    string Issuer,
    string Subject,
    string? Email,
    bool EmailVerified);

public enum SignInFailureReason
{
    None,
    InvalidCredentials,
    UserInactive,
    UnknownIdentity,
    EmailNotVerified,
    AmbiguousBinding,
    InvalidIdentity,
    LockedOut
}

public sealed record SignInResult(
    bool Succeeded,
    SignInFailureReason FailureReason,
    string Message,
    UserDto? User,
    Guid? SessionId,
    DateTimeOffset? AbsoluteExpiresAtUtc,
    bool RequiresOrganizationSelection,
    string? SelectedCompanyId);

public sealed record SessionValidationResult(bool IsValid, string Reason, Guid? UserId);

public sealed record IdentityRuntimeOptions(
    string DemoPassword,
    TimeSpan IdleLifetime,
    TimeSpan AbsoluteLifetime,
    TimeSpan SessionTouchThrottle,
    int MaxConcurrentSessions);
