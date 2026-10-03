using Crm.Domain.Common;

namespace Crm.Domain.Identity;

public sealed class SecurityAuditEvent(
    Guid id,
    DateTimeOffset occurredAtUtc,
    string eventType,
    string outcome,
    Guid? actorUserId,
    Guid? targetUserId,
    Guid? sessionId,
    string correlationId,
    string reason,
    string ipHash,
    string userAgentSummary) : Entity(id)
{
    public DateTimeOffset OccurredAtUtc { get; } = occurredAtUtc;
    public string EventType { get; } = eventType;
    public string Outcome { get; } = outcome;
    public Guid? ActorUserId { get; } = actorUserId;
    public Guid? TargetUserId { get; } = targetUserId;
    public Guid? SessionId { get; } = sessionId;
    public string CorrelationId { get; } = correlationId;
    public string Reason { get; } = reason;
    public string IpHash { get; } = ipHash;
    public string UserAgentSummary { get; } = userAgentSummary;

    private SecurityAuditEvent() : this(Guid.Empty, DateTimeOffset.MinValue, "EF", "EF", null, null, null,
        "EF", string.Empty, string.Empty, string.Empty) { }
}
