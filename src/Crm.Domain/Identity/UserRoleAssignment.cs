using Crm.Domain.Common;

namespace Crm.Domain.Identity;

public enum RoleAssignmentStatus { Active, Revoked, Expired }

public sealed class UserRoleAssignment(
    Guid id,
    Guid crmUserId,
    string roleKey,
    string roleLabel,
    string companyId,
    string scopeType,
    string scopeId,
    string scopeLabel,
    DateTimeOffset validFromUtc,
    DateTimeOffset? validToUtc,
    Guid assignedByUserId,
    string reason) : Entity(id)
{
    public Guid CrmUserId { get; } = crmUserId;
    public string RoleKey { get; } = Required(roleKey, nameof(roleKey));
    public string RoleLabel { get; } = Required(roleLabel, nameof(roleLabel));
    public string CompanyId { get; } = Required(companyId, nameof(companyId));
    public string ScopeType { get; } = Required(scopeType, nameof(scopeType));
    public string ScopeId { get; } = Required(scopeId, nameof(scopeId));
    public string ScopeLabel { get; } = Required(scopeLabel, nameof(scopeLabel));
    public DateTimeOffset ValidFromUtc { get; } = validFromUtc;
    public DateTimeOffset? ValidToUtc { get; } = validToUtc;
    public Guid AssignedByUserId { get; } = assignedByUserId;
    public string Reason { get; } = Required(reason, nameof(reason));
    public RoleAssignmentStatus Status { get; private set; } = RoleAssignmentStatus.Active;
    public DateTimeOffset? RevokedAtUtc { get; private set; }

    private UserRoleAssignment() : this(Guid.Empty, Guid.Empty, "EF", "EF", "EF", "EF", "EF", "EF",
        DateTimeOffset.MinValue, null, Guid.Empty, "EF") { }

    public bool IsEffective(DateTimeOffset nowUtc) =>
        Status == RoleAssignmentStatus.Active && ValidFromUtc <= nowUtc && (ValidToUtc is not { } validToUtc || nowUtc < validToUtc);

    public void Revoke(DateTimeOffset nowUtc)
    {
        if (Status == RoleAssignmentStatus.Revoked) return;
        Status = RoleAssignmentStatus.Revoked;
        RevokedAtUtc = nowUtc;
        Touch();
    }

    private static string Required(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value.Trim();
}
