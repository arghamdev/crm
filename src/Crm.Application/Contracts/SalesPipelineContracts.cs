using Crm.Application.Abstractions;
using Crm.Domain.Sales;

namespace Crm.Application.Contracts;

public enum LeadSlaState { NotApplicable, OnTrack, DueSoon, Overdue, Completed }

public sealed record SalesOwnerOptionDto(Guid UserId, string DisplayName, string BranchId, string RoleLabel);

public sealed record LeadStatusHistoryDto(Guid Id, LeadStatus? FromStatus, LeadStatus ToStatus,
    string Reason, Guid ChangedByUserId, string ChangedByName, DateTimeOffset ChangedAtUtc);
public sealed record LeadDetailsDto(LeadDto Lead, IReadOnlyList<LeadStatusHistoryDto> History,
    IReadOnlyList<SalesOwnerOptionDto> EligibleOwners, bool CanAssign, bool CanUpdate, bool CanConvert);
public sealed record LeadListDto(IReadOnlyList<LeadDto> Items, string? Query, LeadStatus? Status, bool IncludeClosed,
    int OpenCount, int QualifiedCount, int OverdueCount, decimal AverageScore, int Page = 1, int PageSize = PageRequest.DefaultPageSize,
    int TotalCount = 0)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)Math.Max(1, PageSize)));

    // List workspace (views, filters, counters and permissions) — filled by GetLeadListAsync.
    public string View { get; init; } = "all";
    /// <summary>sla (default), score, score-asc, due, name.</summary>
    public string Sort { get; init; } = "sla";
    public string? BranchId { get; init; }
    public string? Source { get; init; }
    public Guid? OwnerUserId { get; init; }
    public int VisibleOpenCount { get; init; }
    public int NeedsActionCount { get; init; }
    public int DuplicateCount { get; init; }
    public int MineCount { get; init; }
    public int NoNextActionCount { get; init; }
    public IReadOnlySet<Guid> DuplicateIds { get; init; } = new HashSet<Guid>();
    public IReadOnlyList<string> Sources { get; init; } = [];
    public IReadOnlyList<(Guid Id, string Name)> Owners { get; init; } = [];
    public bool CanCreate { get; init; }
    public bool CanAssign { get; init; }
    public bool CanUpdate { get; init; }
    public bool CanConvert { get; init; }
}

/// <summary>
/// Lead list request. Views: all, mine, nonext (no next action), overdue (first contact or next action late),
/// needs (no next action, or it is due by the end of today, or first contact due within two hours), duplicates.
/// </summary>
public sealed record LeadListQuery(string? View = null, string? Query = null, LeadStatus? Status = null, string? BranchId = null,
    string? Source = null, Guid? OwnerUserId = null, bool IncludeClosed = false, int Page = 1, int PageSize = 10, string? Sort = null);

public sealed record BulkLeadAssignCommand(IReadOnlyList<Guid> LeadIds, Guid OwnerUserId, string Reason, DateTimeOffset FirstContactDueAtUtc);
public sealed record BulkLeadNextActionCommand(IReadOnlyList<Guid> LeadIds, string NextAction, DateTimeOffset NextActionAtUtc);
public sealed record BulkResultDto(int Succeeded, IReadOnlyList<string> Failures);
public sealed record LeadImportRow(int Line, string? Name, string? Contact, string? Phone, string? Email, string? Source);

public sealed record AssignLeadCommand(Guid OwnerUserId, DateTimeOffset FirstContactDueAtUtc,
    string Reason, long ExpectedVersion);
public sealed record TransitionLeadCommand(LeadStatus TargetStatus, int Score, string Reason,
    string? NextAction, DateTimeOffset? NextActionAtUtc, long ExpectedVersion);
public sealed record ConvertLeadCommand(string OpportunityTitle, decimal Value,
    DateTimeOffset ExpectedCloseAtUtc, long ExpectedVersion, Guid? ExistingCustomerId = null);

public sealed record OpportunityStageHistoryDto(Guid Id, OpportunityStage? FromStage, OpportunityStage ToStage,
    int Probability, string Reason, Guid ChangedByUserId, string ChangedByName, DateTimeOffset ChangedAtUtc);
public sealed record OpportunityActivityDto(Guid Id, OpportunityActivityType Type, string Subject, string Outcome,
    DateTimeOffset OccurredAtUtc, Guid ActorUserId, string ActorName, string? NextAction, DateTimeOffset? NextActionAtUtc);
public sealed record OpportunityDetailsDto(OpportunityDto Opportunity,
    IReadOnlyList<OpportunityStageHistoryDto> StageHistory,
    IReadOnlyList<OpportunityActivityDto> Activities,
    IReadOnlyList<SalesOwnerOptionDto> EligibleOwners,
    bool CanAssign, bool CanUpdate, bool CanClose);
public sealed record OpportunityStageSummaryDto(OpportunityStage Stage, int Count, decimal Value, decimal WeightedValue);
public sealed record PipelineBoardDto(IReadOnlyList<OpportunityDto> Items,
    IReadOnlyList<OpportunityStageSummaryDto> Stages, string? Query, bool IncludeClosed,
    decimal TotalValue, decimal WeightedValue, int OpenCount, int StalledCount,
    int OverdueNextActionCount, decimal AverageAgeDays);

public sealed record CreateOpportunityCommand(Guid CustomerId, string Title, decimal Value, Guid OwnerUserId,
    string BranchId, string? TerritoryId, DateTimeOffset ExpectedCloseAtUtc, string Source,
    string NextAction, DateTimeOffset NextActionAtUtc, string? Competitor = null,
    OpportunityRiskLevel RiskLevel = OpportunityRiskLevel.Medium,
    Guid? ContactId = null, string CurrencyCode = "IRR", OpportunityStage? InitialStage = null, int? Probability = null,
    Guid? OperationId = null);
public sealed record UpdateOpportunityCommand(string Title, decimal Value, DateTimeOffset ExpectedCloseAtUtc,
    string Source, string? Competitor, OpportunityRiskLevel RiskLevel,
    string NextAction, DateTimeOffset NextActionAtUtc, long ExpectedVersion);
public sealed record MoveOpportunityStageCommand(OpportunityStage TargetStage, string Reason, long ExpectedVersion);
public sealed record AssignOpportunityCommand(Guid OwnerUserId, string Reason, long ExpectedVersion);
public sealed record AddOpportunityActivityCommand(OpportunityActivityType Type, string Subject, string Outcome,
    DateTimeOffset OccurredAtUtc, string? NextAction, DateTimeOffset? NextActionAtUtc, long ExpectedVersion);
