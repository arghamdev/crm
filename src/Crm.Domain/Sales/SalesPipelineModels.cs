using Crm.Domain.Common;
using Crm.Domain.Organization;

namespace Crm.Domain.Sales;

public enum OpportunityActivityType { Call, Meeting, Email, Visit, Task, Note }

public sealed class LeadStatusHistory(
    Guid id, string companyId, string branchId, string? territoryId, Guid leadId,
    LeadStatus? fromStatus, LeadStatus toStatus, string reason, Guid changedByUserId,
    DateTimeOffset changedAtUtc) : Entity(id), IOrganizationScoped
{
    public string CompanyId { get; } = Required(companyId, nameof(companyId));
    public string BranchId { get; } = Required(branchId, nameof(branchId));
    public string? TerritoryId { get; } = Optional(territoryId);
    public Guid LeadId { get; } = leadId;
    public LeadStatus? FromStatus { get; } = fromStatus;
    public LeadStatus ToStatus { get; } = toStatus;
    public string Reason { get; } = Required(reason, nameof(reason));
    public Guid ChangedByUserId { get; } = changedByUserId;
    public DateTimeOffset ChangedAtUtc { get; } = changedAtUtc;

    private LeadStatusHistory() : this(Guid.Empty, "EF", "EF", null, Guid.Empty, null, LeadStatus.New, "EF", Guid.Empty, DateTimeOffset.MinValue) { }
    private static string Required(string value, string name) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value.Trim();
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class OpportunityStageHistory(
    Guid id, string companyId, string branchId, string? territoryId, Guid opportunityId,
    OpportunityStage? fromStage, OpportunityStage toStage, int probability, string reason,
    Guid changedByUserId, DateTimeOffset changedAtUtc) : Entity(id), IOrganizationScoped
{
    public string CompanyId { get; } = Required(companyId, nameof(companyId));
    public string BranchId { get; } = Required(branchId, nameof(branchId));
    public string? TerritoryId { get; } = Optional(territoryId);
    public Guid OpportunityId { get; } = opportunityId;
    public OpportunityStage? FromStage { get; } = fromStage;
    public OpportunityStage ToStage { get; } = toStage;
    public int Probability { get; } = Math.Clamp(probability, 0, 100);
    public string Reason { get; } = Required(reason, nameof(reason));
    public Guid ChangedByUserId { get; } = changedByUserId;
    public DateTimeOffset ChangedAtUtc { get; } = changedAtUtc;

    private OpportunityStageHistory() : this(Guid.Empty, "EF", "EF", null, Guid.Empty, null, OpportunityStage.Identified, 10, "EF", Guid.Empty, DateTimeOffset.MinValue) { }
    private static string Required(string value, string name) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value.Trim();
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class OpportunityActivity(
    Guid id, string companyId, string branchId, string? territoryId, Guid opportunityId,
    OpportunityActivityType type, string subject, string outcome, DateTimeOffset occurredAtUtc,
    Guid actorUserId, string? nextAction = null, DateTimeOffset? nextActionAtUtc = null) : Entity(id), IOrganizationScoped
{
    public string CompanyId { get; } = Required(companyId, nameof(companyId));
    public string BranchId { get; } = Required(branchId, nameof(branchId));
    public string? TerritoryId { get; } = Optional(territoryId);
    public Guid OpportunityId { get; } = opportunityId;
    public OpportunityActivityType Type { get; } = type;
    public string Subject { get; } = Required(subject, nameof(subject));
    public string Outcome { get; } = Required(outcome, nameof(outcome));
    public DateTimeOffset OccurredAtUtc { get; } = occurredAtUtc;
    public Guid ActorUserId { get; } = actorUserId;
    public string? NextAction { get; } = Optional(nextAction);
    public DateTimeOffset? NextActionAtUtc { get; } = nextActionAtUtc;

    private OpportunityActivity() : this(Guid.Empty, "EF", "EF", null, Guid.Empty, OpportunityActivityType.Note, "EF", "EF", DateTimeOffset.MinValue, Guid.Empty) { }
    private static string Required(string value, string name) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value.Trim();
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
