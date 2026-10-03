using Crm.Domain.Common;

namespace Crm.Domain.Identity;

public sealed class UserSession(
    Guid id,
    Guid crmUserId,
    DateTimeOffset issuedAtUtc,
    TimeSpan idleLifetime,
    TimeSpan absoluteLifetime,
    long securityVersionAtIssue,
    string ipHash,
    string userAgentSummary) : Entity(id)
{
    public Guid CrmUserId { get; } = crmUserId;
    public DateTimeOffset IssuedAtUtc { get; } = issuedAtUtc;
    public DateTimeOffset LastSeenAtUtc { get; private set; } = issuedAtUtc;
    public DateTimeOffset IdleExpiresAtUtc { get; private set; } = issuedAtUtc.Add(idleLifetime);
    public DateTimeOffset AbsoluteExpiresAtUtc { get; } = issuedAtUtc.Add(absoluteLifetime);
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public string? RevokeReason { get; private set; }
    public long SecurityVersionAtIssue { get; } = securityVersionAtIssue;
    public string IpHash { get; } = ipHash;
    public string UserAgentSummary { get; } = userAgentSummary;
    public string? SelectedCompanyId { get; private set; }
    public string? SelectedBranchId { get; private set; }
    public string? SelectedTerritoryId { get; private set; }

    private UserSession() : this(Guid.Empty, Guid.Empty, DateTimeOffset.MinValue, TimeSpan.Zero, TimeSpan.Zero, 0, string.Empty, string.Empty) { }

    public bool IsValid(DateTimeOffset nowUtc, long currentSecurityVersion) =>
        RevokedAtUtc is null && nowUtc < IdleExpiresAtUtc && nowUtc < AbsoluteExpiresAtUtc &&
        SecurityVersionAtIssue == currentSecurityVersion;

    public void TouchSession(DateTimeOffset nowUtc, TimeSpan idleLifetime, TimeSpan writeThrottle)
    {
        if (nowUtc - LastSeenAtUtc < writeThrottle) return;
        LastSeenAtUtc = nowUtc;
        var candidate = nowUtc.Add(idleLifetime);
        IdleExpiresAtUtc = candidate < AbsoluteExpiresAtUtc ? candidate : AbsoluteExpiresAtUtc;
        Touch();
    }

    public void Revoke(DateTimeOffset nowUtc, string reason)
    {
        if (RevokedAtUtc is not null) return;
        RevokedAtUtc = nowUtc;
        RevokeReason = string.IsNullOrWhiteSpace(reason) ? "Revoked" : reason.Trim();
        Touch();
    }

    public void SelectOrganizationContext(string companyId, string? branchId, string? territoryId)
    {
        SelectedCompanyId = Required(companyId, nameof(companyId));
        SelectedBranchId = Optional(branchId);
        SelectedTerritoryId = Optional(territoryId);
        Touch();
    }

    private static string Required(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value.Trim();
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
