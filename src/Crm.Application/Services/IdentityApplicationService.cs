using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Domain.Identity;
using Crm.Domain.Organization;

namespace Crm.Application.Services;

public sealed class IdentityApplicationService(
    ICrmDataStore store,
    IAccessSnapshotService access,
    IdentityRuntimeOptions options,
    ILoginAttemptGuard? loginGuard = null,
    ICrmQuerySource? querySource = null) : IIdentityApplicationService
{
    private static readonly Dictionary<string, (string Label, IReadOnlySet<string> ScopeTypes)> Roles =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Executive"] = ("مدیرعامل ـ گزارش خواندنی", new HashSet<string>(["Company", "Branch"], StringComparer.OrdinalIgnoreCase)),
            ["CompanyMember"] = ("عضو شرکت", new HashSet<string>(["Company"], StringComparer.OrdinalIgnoreCase)),
            ["SalesManager"] = ("مدیر فروش", new HashSet<string>(["Company", "Branch"], StringComparer.OrdinalIgnoreCase)),
            ["SalesSupervisor"] = ("سرپرست فروش", new HashSet<string>(["Branch", "Territory"], StringComparer.OrdinalIgnoreCase)),
            ["SalesExpert"] = ("کارشناس فروش", new HashSet<string>(["Branch", "Territory"], StringComparer.OrdinalIgnoreCase))
        };

    public IReadOnlyList<UserDto> GetUsers() => store.Read(data =>
    {
        var now = DateTimeOffset.UtcNow;
        var assignments = data.UserRoleAssignments.ToLookup(x => x.CrmUserId);
        var identities = data.ExternalIdentities.Where(x => x.IsActive).Select(x => x.CrmUserId).ToHashSet();
        // Only live sessions are needed for the count; the session table keeps every historical login.
        var sessions = data.Find<UserSession>(x => x.RevokedAtUtc == null && x.AbsoluteExpiresAtUtc > now).ToLookup(x => x.CrmUserId);
        return data.Users.OrderBy(x => x.DisplayName)
            .Select(x => MapUser(x, now, assignments[x.Id], identities.Contains(x.Id), sessions[x.Id])).ToList();
    });

    public UserDetailsDto? GetUser(Guid id) => GetUserAsync(id).GetAwaiter().GetResult();

    public async Task<UserDetailsDto?> GetUserAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var details = store.Read(data =>
        {
            var user = data.Find<CrmUser>(x => x.Id == id).SingleOrDefault();
            if (user is null) return null;
            return new UserDetailsDto(
                MapUser(data, user, DateTimeOffset.UtcNow),
                data.Find<UserRoleAssignment>(x => x.CrmUserId == id).OrderByDescending(x => x.ValidFromUtc).Select(MapRole).ToList(),
                data.Find<UserSession>(x => x.CrmUserId == id).OrderByDescending(x => x.IssuedAtUtc).Take(50).Select(MapSession).ToList(),
                data.Find<ExternalIdentity>(x => x.CrmUserId == id).OrderByDescending(x => x.LinkedAtUtc).Select(MapIdentity).ToList(),
                []);
        });
        if (details is null) return null;
        // The audit log is the largest table; only the latest page is read, ordered and limited in the database.
        var source = querySource ?? store as ICrmQuerySource;
        var audit = source is null ? [] : await source.ToListAsync(source.Query<SecurityAuditEvent>()
            .Where(x => x.TargetUserId == id || x.ActorUserId == id)
            .OrderByDescending(x => x.OccurredAtUtc).ThenBy(x => x.Id).Take(30), cancellationToken);
        return details with { AuditEvents = audit.Select(MapAudit).ToList() };
    }

    public UserDto CreatePendingUser(CreatePendingUserCommand command, Guid actorUserId, IdentityRequestContext context) => store.Write(data =>
    {
        var normalizedEmail = NormalizeEmail(command.Email);
        var normalizedUserName = Required(command.UserName, nameof(command.UserName)).ToLowerInvariant();
        if (data.Users.Any(x => x.NormalizedEmail == normalizedEmail || x.UserName == normalizedUserName))
            throw new InvalidOperationException("Email or user name already exists.");
        var user = new CrmUser(Guid.NewGuid(), command.DisplayName, normalizedUserName, normalizedEmail, command.EmployeeNumber);
        data.Users.Add(user);
        var defaultCompany = data.Companies.FirstOrDefault(x => x.Status == OrganizationStatus.Active) ??
            throw new InvalidOperationException("No active company is configured.");
        data.UserRoleAssignments.Add(new UserRoleAssignment(Guid.NewGuid(), user.Id, "CompanyMember", "عضو شرکت",
            defaultCompany.CompanyId, "Company", defaultCompany.CompanyId, defaultCompany.Name, context.NowUtc, null,
            actorUserId, "عضویت پایه هنگام ایجاد کاربر"));
        AddAudit(data, context, "UserCreated", "Success", actorUserId, user.Id, null, "Pending activation user created.");
        return MapUser(data, user, context.NowUtc);
    });

    public UserDto ChangeStatus(Guid id, UserStatus status, long expectedSecurityVersion, Guid actorUserId, IdentityRequestContext context) => store.Write(data =>
    {
        var user = data.Users.Single(x => x.Id == id);
        EnsureCurrentVersion(user, expectedSecurityVersion);
        if (user.Id == actorUserId && status != UserStatus.Active)
            throw new InvalidOperationException("You cannot suspend your own active account.");
        if (status == UserStatus.Active && !data.Find<ExternalIdentity>(x => x.CrmUserId == id).Any(x => x.IsActive))
            throw new InvalidOperationException("User activation requires a linked external identity.");
        switch (status)
        {
            case UserStatus.Active: user.Activate(); break;
            case UserStatus.Suspended: user.Suspend(); break;
            case UserStatus.Disabled: user.Disable(); break;
            default: throw new InvalidOperationException("Unsupported status transition.");
        }
        if (status != UserStatus.Active)
            foreach (var session in data.Find<UserSession>(x => x.CrmUserId == id && x.RevokedAtUtc == null))
                session.Revoke(context.NowUtc, $"User status changed to {status}.");
        access.Invalidate(id);
        AddAudit(data, context, "UserStatusChanged", "Success", actorUserId, id, null, status.ToString());
        return MapUser(data, user, context.NowUtc);
    });

    public RoleAssignmentDto AssignRole(Guid id, AssignRoleCommand command, Guid actorUserId, IdentityRequestContext context) => store.Write(data =>
    {
        var user = data.Users.Single(x => x.Id == id);
        EnsureCurrentVersion(user, command.ExpectedSecurityVersion);
        if (!Roles.TryGetValue(command.RoleKey, out var role)) throw new InvalidOperationException("Unknown role.");
        if (!role.ScopeTypes.Contains(command.ScopeType)) throw new InvalidOperationException("Role is not valid for this scope type.");
        var scope = ResolveScope(data, command.ScopeType, command.ScopeId, context.NowUtc);
        if (scope is null) throw new InvalidOperationException("Scope type and identifier do not match an active organization node.");
        if (command.ValidToUtc is { } validToUtc && validToUtc <= context.NowUtc) throw new InvalidOperationException("Validity end must be in the future.");
        var duplicate = data.UserRoleAssignments.Any(x => x.CrmUserId == id &&
            x.RoleKey.Equals(command.RoleKey, StringComparison.OrdinalIgnoreCase) &&
            x.ScopeType.Equals(command.ScopeType, StringComparison.OrdinalIgnoreCase) &&
            x.ScopeId.Equals(command.ScopeId, StringComparison.OrdinalIgnoreCase) && x.IsEffective(context.NowUtc));
        if (duplicate) throw new InvalidOperationException("This effective role assignment already exists.");
        var assignment = new UserRoleAssignment(Guid.NewGuid(), id, command.RoleKey, role.Label, scope.Value.CompanyId, command.ScopeType,
            Required(command.ScopeId, nameof(command.ScopeId)), scope.Value.Label, context.NowUtc,
            command.ValidToUtc, actorUserId, Required(command.Reason, nameof(command.Reason)));
        data.UserRoleAssignments.Add(assignment);
        user.IncrementSecurityVersion();
        RevokeSessions(data, id, context.NowUtc, "Access assignment changed.");
        access.Invalidate(id);
        AddAudit(data, context, "RoleAssigned", "Success", actorUserId, id, null, $"{assignment.RoleKey}@{assignment.ScopeType}:{assignment.ScopeId}");
        return MapRole(assignment);
    });

    public void RevokeRole(Guid userId, Guid assignmentId, long expectedSecurityVersion, Guid actorUserId, IdentityRequestContext context) => store.Write(data =>
    {
        var user = data.Users.Single(x => x.Id == userId);
        EnsureCurrentVersion(user, expectedSecurityVersion);
        var assignment = data.UserRoleAssignments.Single(x => x.Id == assignmentId && x.CrmUserId == userId);
        assignment.Revoke(context.NowUtc);
        user.IncrementSecurityVersion();
        RevokeSessions(data, userId, context.NowUtc, "Access assignment revoked.");
        access.Invalidate(userId);
        AddAudit(data, context, "RoleRevoked", "Success", actorUserId, userId, null, assignment.RoleKey);
        return true;
    });

    public void RevokeSession(Guid userId, Guid sessionId, Guid actorUserId, IdentityRequestContext context) => store.Write(data =>
    {
        var session = data.Find<UserSession>(x => x.Id == sessionId && x.CrmUserId == userId).Single();
        session.Revoke(context.NowUtc, "Revoked by administrator.");
        AddAudit(data, context, "SessionRevoked", "Success", actorUserId, userId, sessionId, "Administrator action.");
        return true;
    });

    public int RevokeOtherSessions(Guid userId, Guid currentSessionId, IdentityRequestContext context) => store.Write(data =>
    {
        var sessions = data.Find<UserSession>(x => x.CrmUserId == userId && x.Id != currentSessionId && x.RevokedAtUtc == null);
        foreach (var session in sessions) session.Revoke(context.NowUtc, "Signed out from another session.");
        AddAudit(data, context, "OtherSessionsRevoked", "Success", userId, userId, currentSessionId, $"Count={sessions.Count}");
        return sessions.Count;
    });

    public SignInResult AuthenticateDemo(string userName, string password, IdentityRequestContext context)
    {
        var normalizedUserName = userName?.Trim() ?? string.Empty;
        // Checked before the password so a locked account cannot be probed, even with the right password.
        if (normalizedUserName.Length > 0 && loginGuard?.LockedUntil(normalizedUserName, context.NowUtc) is { } lockedUntil)
            return Failed(SignInFailureReason.LockedOut, LockedOutMessage(lockedUntil, context.NowUtc), context, "Login attempt during lockout.");
        if (normalizedUserName.Length == 0 || !FixedTimeEquals(password, options.DemoPassword))
            return FailedAttempt(normalizedUserName, context, "Invalid demo credentials.");
        var result = store.Write(data =>
        {
            var user = data.Users.SingleOrDefault(x => x.UserName.Equals(normalizedUserName, StringComparison.OrdinalIgnoreCase));
            if (user is null) return FailedInside(data, SignInFailureReason.InvalidCredentials, "نام کاربری یا رمز نمونه صحیح نیست.", context, null, "Unknown demo user.");
            if (!user.IsActiveAt(context.NowUtc)) return FailedInside(data, SignInFailureReason.UserInactive, "حساب کاربری فعال نیست.", context, user.Id, user.Status.ToString());
            return CreateSession(data, user, context, "DemoPassword");
        });
        if (result.Succeeded) loginGuard?.Reset(normalizedUserName);
        else if (result.FailureReason == SignInFailureReason.InvalidCredentials) RecordGuardFailure(normalizedUserName, context);
        return result;
    }

    public SignInResult AuthenticateExternal(ExternalIdentityDescriptor identity, IdentityRequestContext context)
    {
        if (string.IsNullOrWhiteSpace(identity.Issuer) || string.IsNullOrWhiteSpace(identity.Subject))
            return Failed(SignInFailureReason.InvalidIdentity, "شناسه هویت خارجی معتبر نیست.", context, identity.ProviderKey);
        return store.Write(data =>
        {
            var exact = data.ExternalIdentities.SingleOrDefault(x => x.Matches(identity.Issuer, identity.Subject));
            if (exact is not null)
            {
                var linkedUser = data.Users.Single(x => x.Id == exact.CrmUserId);
                if (!linkedUser.IsActiveAt(context.NowUtc))
                    return FailedInside(data, SignInFailureReason.UserInactive, "حساب کاربری فعال نیست.", context, linkedUser.Id, linkedUser.Status.ToString());
                exact.MarkSeen(context.NowUtc);
                return CreateSession(data, linkedUser, context, identity.ProviderKey);
            }
            if (!identity.EmailVerified || string.IsNullOrWhiteSpace(identity.Email))
                return FailedInside(data, SignInFailureReason.EmailNotVerified, "ایمیل تأییدشده برای اتصال اولیه لازم است.", context, null,
                    $"Missing verified email; identity={IdentityFingerprint(identity)}");
            var normalizedEmail = NormalizeEmail(identity.Email);
            var candidates = data.Users.Where(x => x.Status == UserStatus.PendingActivation && x.NormalizedEmail == normalizedEmail).ToList();
            if (candidates.Count == 0)
                return FailedInside(data, SignInFailureReason.UnknownIdentity, "برای این هویت، کاربر در انتظار فعال‌سازی یافت نشد.", context, null,
                    $"No pending candidate; identity={IdentityFingerprint(identity)}");
            if (candidates.Count > 1)
                return FailedInside(data, SignInFailureReason.AmbiguousBinding, "اتصال هویت مبهم است و باید توسط مدیر بررسی شود.", context, null,
                    $"Multiple pending candidates; identity={IdentityFingerprint(identity)}");
            var user = candidates[0];
            data.ExternalIdentities.Add(new ExternalIdentity(Guid.NewGuid(), user.Id, identity.ProviderKey, identity.Issuer,
                identity.Subject, normalizedEmail, context.NowUtc));
            user.Activate();
            access.Invalidate(user.Id);
            AddAudit(data, context, "ExternalIdentityLinked", "Success", user.Id, user.Id, null, identity.ProviderKey);
            return CreateSession(data, user, context, identity.ProviderKey);
        });
    }

    public SessionValidationResult ValidateSession(Guid sessionId, Guid userId, long securityVersion, IdentityRequestContext context) => store.Write(data =>
    {
        var user = data.Find<CrmUser>(x => x.Id == userId).SingleOrDefault();
        var session = data.Find<UserSession>(x => x.Id == sessionId && x.CrmUserId == userId).SingleOrDefault();
        if (user is null || !user.IsActiveAt(context.NowUtc))
        {
            AddAudit(data, context, "SessionValidation", "Failure", userId, userId, sessionId, "UserInactive");
            return new SessionValidationResult(false, "UserInactive", userId);
        }
        if (session is null)
        {
            AddAudit(data, context, "SessionValidation", "Failure", userId, userId, sessionId, "SessionNotFound");
            return new SessionValidationResult(false, "SessionNotFound", userId);
        }
        if (securityVersion != user.SecurityVersion || !session.IsValid(context.NowUtc, user.SecurityVersion))
        {
            AddAudit(data, context, "SessionValidation", "Failure", userId, userId, sessionId, "SessionExpiredOrRevoked");
            return new SessionValidationResult(false, "SessionExpiredOrRevoked", userId);
        }
        session.TouchSession(context.NowUtc, options.IdleLifetime, options.SessionTouchThrottle);
        return new SessionValidationResult(true, "Valid", userId);
    });

    public void RecordAccessDenied(Guid userId, string permission, IdentityRequestContext context) => store.Write(data =>
    {
        AddAudit(data, context, "AccessDenied", "Failure", userId, userId, null, permission);
        return true;
    });

    public void RecordAuthenticationFailure(string reasonCode, IdentityRequestContext context) => store.Write(data =>
    {
        AddAudit(data, context, "OidcRemoteFailure", "Failure", null, null, null, reasonCode);
        return true;
    });

    private SignInResult CreateSession(CrmDataSet data, CrmUser user, IdentityRequestContext context, string provider)
    {
        var effectiveAssignments = data.Find<UserRoleAssignment>(x => x.CrmUserId == user.Id)
            .Where(x => x.IsEffective(context.NowUtc) &&
                !x.RoleKey.Equals("CompanyMember", StringComparison.OrdinalIgnoreCase)).ToList();
        var companyIds = effectiveAssignments.Select(x => x.CompanyId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (companyIds.Count == 0)
            return FailedInside(data, SignInFailureReason.UserInactive, "دامنه عملیاتی فعال برای کاربر تعریف نشده است.", context, user.Id, "Missing operational company scope.");
        var active = data.Find<UserSession>(x => x.CrmUserId == user.Id && x.RevokedAtUtc == null)
            .Where(x => x.IsValid(context.NowUtc, user.SecurityVersion))
            .OrderBy(x => x.LastSeenAtUtc).ToList();
        while (active.Count >= options.MaxConcurrentSessions)
        {
            active[0].Revoke(context.NowUtc, "Concurrent session limit reached.");
            active.RemoveAt(0);
        }
        var session = new UserSession(Guid.NewGuid(), user.Id, context.NowUtc, options.IdleLifetime, options.AbsoluteLifetime,
            user.SecurityVersion, context.IpHash, context.UserAgentSummary);
        if (companyIds.Count == 1)
        {
            var companyId = companyIds[0];
            var companyAssignments = effectiveAssignments.Where(x => x.CompanyId.Equals(companyId, StringComparison.OrdinalIgnoreCase)).ToList();
            var hasCompanyWide = companyAssignments.Any(x => x.ScopeType.Equals("Company", StringComparison.OrdinalIgnoreCase));
            var branches = companyAssignments.Where(x => x.ScopeType.Equals("Branch", StringComparison.OrdinalIgnoreCase))
                .Select(x => x.ScopeId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var territories = companyAssignments.Where(x => x.ScopeType.Equals("Territory", StringComparison.OrdinalIgnoreCase))
                .Select(x => x.ScopeId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            session.SelectOrganizationContext(companyId, !hasCompanyWide && branches.Count == 1 ? branches[0] : null,
                !hasCompanyWide && territories.Count == 1 ? territories[0] : null);
        }
        data.Append(session);
        user.RecordSuccessfulLogin(context.NowUtc);
        AddAudit(data, context, "SignIn", "Success", user.Id, user.Id, session.Id, provider);
        return new SignInResult(true, SignInFailureReason.None, "ورود موفق بود.", MapUser(data, user, context.NowUtc),
            session.Id, session.AbsoluteExpiresAtUtc, session.SelectedCompanyId is null, session.SelectedCompanyId);
    }

    private SignInResult FailedAttempt(string userName, IdentityRequestContext context, string detail)
    {
        var lockedUntil = RecordGuardFailure(userName, context);
        return lockedUntil is { } until
            ? Failed(SignInFailureReason.LockedOut, LockedOutMessage(until, context.NowUtc), context, detail + " Lockout started.")
            : Failed(SignInFailureReason.InvalidCredentials, "نام کاربری یا رمز نمونه صحیح نیست.", context, detail);
    }

    private DateTimeOffset? RecordGuardFailure(string userName, IdentityRequestContext context) =>
        userName.Length == 0 ? null : loginGuard?.RecordFailure(userName, context.NowUtc);

    private static string LockedOutMessage(DateTimeOffset lockedUntil, DateTimeOffset nowUtc) =>
        $"به‌دلیل تلاش‌های ناموفق مکرر، ورود به این حساب تا {Math.Max(1, (int)Math.Ceiling((lockedUntil - nowUtc).TotalMinutes))} دقیقهٔ دیگر موقتاً مسدود است.";

    private SignInResult Failed(SignInFailureReason reason, string message, IdentityRequestContext context, string detail) => store.Write(data =>
        FailedInside(data, reason, message, context, null, detail));

    private static SignInResult FailedInside(CrmDataSet data, SignInFailureReason reason, string message, IdentityRequestContext context, Guid? userId, string detail)
    {
        AddAudit(data, context, "SignIn", "Failure", userId, userId, null, $"{reason}:{detail}");
        return new SignInResult(false, reason, message, null, null, null, false, null);
    }

    private static void RevokeSessions(CrmDataSet data, Guid userId, DateTimeOffset nowUtc, string reason)
    {
        foreach (var session in data.Find<UserSession>(x => x.CrmUserId == userId && x.RevokedAtUtc == null)) session.Revoke(nowUtc, reason);
    }

    private static void AddAudit(CrmDataSet data, IdentityRequestContext context, string eventType, string outcome,
        Guid? actorId, Guid? targetId, Guid? sessionId, string reason) => data.Append(
        new SecurityAuditEvent(Guid.NewGuid(), context.NowUtc, eventType, outcome, actorId, targetId, sessionId,
            context.CorrelationId, reason, context.IpHash, context.UserAgentSummary));

    private static UserDto MapUser(CrmDataSet data, CrmUser user, DateTimeOffset nowUtc) => MapUser(user, nowUtc,
        data.Find<UserRoleAssignment>(x => x.CrmUserId == user.Id),
        data.Find<ExternalIdentity>(x => x.CrmUserId == user.Id).Any(x => x.IsActive),
        data.Find<UserSession>(x => x.CrmUserId == user.Id && x.RevokedAtUtc == null));

    private static UserDto MapUser(CrmUser user, DateTimeOffset nowUtc, IEnumerable<UserRoleAssignment> userAssignments,
        bool hasActiveIdentity, IEnumerable<UserSession> unrevokedSessions)
    {
        var assignments = userAssignments.Where(x => x.IsEffective(nowUtc)).ToList();
        return new UserDto(user.Id, user.DisplayName, user.UserName, user.NormalizedEmail, user.EmployeeNumber,
            assignments.Select(x => x.RoleLabel).Distinct().ToList(), assignments.Select(x => x.ScopeLabel).Distinct().ToList(),
            user.Status, user.SecurityVersion, hasActiveIdentity,
            unrevokedSessions.Count(x => x.IsValid(nowUtc, user.SecurityVersion)), user.LastLoginAtUtc);
    }

    private static UserSessionDto MapSession(UserSession x) => new(x.Id, x.IssuedAtUtc, x.LastSeenAtUtc, x.IdleExpiresAtUtc,
        x.AbsoluteExpiresAtUtc, x.RevokedAtUtc, x.RevokeReason, x.IpHash, x.UserAgentSummary,
        x.SelectedCompanyId, x.SelectedBranchId, x.SelectedTerritoryId);
    private static RoleAssignmentDto MapRole(UserRoleAssignment x) => new(x.Id, x.RoleKey, x.RoleLabel, x.CompanyId, x.ScopeType, x.ScopeId,
        x.ScopeLabel, x.ValidFromUtc, x.ValidToUtc,
        x.Status == RoleAssignmentStatus.Active && x.ValidToUtc is { } validToUtc && validToUtc <= DateTimeOffset.UtcNow
            ? RoleAssignmentStatus.Expired : x.Status,
        x.Reason);
    private static ExternalIdentityDto MapIdentity(ExternalIdentity x) => new(x.Id, x.ProviderKey, x.Issuer, x.Subject,
        x.EmailAtLink, x.LinkedAtUtc, x.LastSeenAtUtc, x.IsActive);
    private static SecurityAuditEventDto MapAudit(SecurityAuditEvent x) => new(x.Id, x.OccurredAtUtc, x.EventType, x.Outcome,
        x.ActorUserId, x.TargetUserId, x.SessionId, x.CorrelationId, x.Reason);
    private static string Required(string value, string name) => string.IsNullOrWhiteSpace(value)
        ? throw new ArgumentException("Value is required.", name) : value.Trim();
    private static bool FixedTimeEquals(string? supplied, string expected) =>
        System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(supplied ?? string.Empty), System.Text.Encoding.UTF8.GetBytes(expected));
    private static string NormalizeEmail(string email) => Required(email, nameof(email)).ToUpperInvariant();
    private static string IdentityFingerprint(ExternalIdentityDescriptor identity)
    {
        var source = $"{identity.Issuer.TrimEnd('/')}|{identity.Subject}";
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source)))[..16];
    }
    private static void EnsureCurrentVersion(CrmUser user, long expectedSecurityVersion)
    {
        if (user.SecurityVersion != expectedSecurityVersion)
            throw new InvalidOperationException("The user access record changed. Reload and try again.");
    }
    private static (string CompanyId, string Label)? ResolveScope(CrmDataSet data, string type, string id, DateTimeOffset nowUtc)
    {
        if (type.Equals("Company", StringComparison.OrdinalIgnoreCase))
        {
            var company = data.Companies.SingleOrDefault(x => x.CompanyId.Equals(id, StringComparison.OrdinalIgnoreCase) &&
                x.Status == OrganizationStatus.Active);
            return company is null ? null : (company.CompanyId, company.Name);
        }
        if (type.Equals("Branch", StringComparison.OrdinalIgnoreCase))
        {
            var branch = data.OrganizationUnits.SingleOrDefault(x => x.UnitId.Equals(id, StringComparison.OrdinalIgnoreCase) &&
                x.Type == OrganizationUnitType.Branch && x.Status == OrganizationStatus.Active);
            return branch is null ? null : (branch.CompanyId, branch.Name);
        }
        if (type.Equals("Territory", StringComparison.OrdinalIgnoreCase))
        {
            var territory = data.Territories.SingleOrDefault(x => x.TerritoryId.Equals(id, StringComparison.OrdinalIgnoreCase) && x.IsEffective(nowUtc));
            return territory is null ? null : (territory.CompanyId, territory.Name);
        }
        return null;
    }
}
