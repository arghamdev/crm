using Crm.Domain.Accounts;
using Crm.Domain.FollowUps;

namespace Crm.Application.Contracts;

public sealed record FollowUpOptionDto(string Value, string Label);

// ───────────── List (مرکز پیگیری) ─────────────

/// <summary>Views: all (open), mine, nonext (no next action), overdue, waiting, referrals (pending referrals to me), closed.</summary>
public sealed record FollowUpListQuery(string? View = null, string? Query = null, string? CaseType = null, FollowUpStatus? Status = null,
    FollowUpPriority? Priority = null, string? BranchId = null, Guid? OwnerUserId = null, string? Sort = null, int Page = 1, int PageSize = 10,
    Guid? CustomerId = null);

public sealed record FollowUpRowDto(Guid Id, string Code, string Subject, Guid CustomerId, string CustomerName, string CaseType, FollowUpPriority Priority,
    FollowUpStatus Status, int Progress, string? CurrentStage, string? Owner, string? StageOwner, string? NextAction, DateTimeOffset? NextActionAtUtc,
    DateTimeOffset? NearestDueUtc, bool IsOverdue, bool IsPaused, int ReopenCount, bool PendingReferral, string BranchId);

public sealed record FollowUpListDto(IReadOnlyList<FollowUpRowDto> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)Math.Max(1, PageSize)));
    public string View { get; init; } = "all";
    public string? Query { get; init; }
    public string? CaseType { get; init; }
    public FollowUpStatus? Status { get; init; }
    public FollowUpPriority? Priority { get; init; }
    public string? BranchId { get; init; }
    public Guid? OwnerUserId { get; init; }
    public string Sort { get; init; } = "due";
    public int OpenCount { get; init; }
    public int MineCount { get; init; }
    public int NoNextActionCount { get; init; }
    public int OverdueCount { get; init; }
    public int WaitingCount { get; init; }
    public int ReferralCount { get; init; }
    public IReadOnlyList<(Guid Id, string Name)> Owners { get; init; } = [];
    public bool CanCreate { get; init; }
    public bool CanSupervise { get; init; }
    public bool CanConfigure { get; init; }
}

// ───────────── Create (۱. ثبت پرونده پیگیری) ─────────────

public sealed record FollowUpPartInput(string? PartCode, string? Description, decimal? Quantity, string? Unit);

public sealed record CreateFollowUpCommand(
    string? Subject,
    Guid CustomerId,
    Guid? ContactId,
    string? CaseType,
    Guid? TemplateId,
    string? BranchId,
    Guid? OwnerUserId,
    Guid? QueueId,
    FollowUpChannel Channel,
    FollowUpPriority Priority,
    string? PriorityReason,
    FollowUpRelatedKind RelatedKind,
    Guid? RelatedId,
    string? RelatedCode,
    Guid? DealerId,
    string? Description,
    string? ExpectedOutcome,
    string? PartFamily,
    string? Language,
    IReadOnlyList<FollowUpPartInput>? Parts,
    IReadOnlyDictionary<string, string?>? ExtraFields,
    Guid? OperationId,
    bool AllowDuplicate = false,
    string? DuplicateReason = null,
    Guid? ParentCaseId = null);

public sealed record FollowUpDuplicateDto(Guid Id, string Code, string Subject, FollowUpStatus Status, string Reason);

public sealed record FollowUpCreateOptions(
    Guid? CustomerId,
    string? CustomerName,
    IReadOnlyList<FollowUpOptionDto> Customers,
    IReadOnlyList<FollowUpOptionDto> Contacts,
    IReadOnlyList<FollowUpOptionDto> Templates,
    IReadOnlyList<FollowUpOptionDto> Branches,
    IReadOnlyList<FollowUpOptionDto> Owners,
    IReadOnlyList<FollowUpOptionDto> Queues,
    IReadOnlyList<FollowUpOptionDto> Dealers,
    IReadOnlyList<(FollowUpRelatedKind Kind, Guid Id, string Label)> RelatedRecords,
    IReadOnlyList<string> ExtraFields,
    string CaseType,
    string? SuggestedOwner,
    string SlaPreview,
    string? ClosingCriteria)
{
    /// <summary>Queue and person the assignment rules pick for this customer and type (preselected in the form).</summary>
    public Guid? SuggestedQueueId { get; init; }
    public Guid? SuggestedOwnerId { get; init; }
}

// ───────────── Case page ─────────────

public sealed record FollowUpChecklistDto(Guid Id, string Title, bool IsDone, string? DoneBy, DateTimeOffset? DoneAtUtc);

public sealed record FollowUpStageDto(Guid Id, int Order, string Name, int Weight, bool Required, FollowUpStageStatus Status, int Progress,
    string ResponsibleRole, Guid? ResponsibleUserId, string? Responsible, DateTimeOffset? DueAtUtc, bool IsOverdue, DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc, string? ReturnReason, string? SkipReason, string? Condition, IReadOnlyList<FollowUpChecklistDto> Checklist,
    int Contribution, bool CanComplete, string? CompleteBlockedReason);

public sealed record FollowUpPartDto(Guid Id, string PartCode, string? AlternateCode, string? Description, string? Compatibility, decimal Quantity, string Unit, string? Warehouse,
    string? SerialOrBatch, FollowUpItemStatus Status, decimal DeliveredQuantity, string? Note);

public sealed record FollowUpReferralDto(Guid Id, Guid CaseId, string CaseCode, string CaseSubject, string CustomerName, ReferralScope Scope, string? StageName,
    string From, string To, Guid ToUserId, string? ToBranch, string? ToTeam, string Reason, DateTimeOffset SentAtUtc, DateTimeOffset AcceptDueAtUtc,
    ReferralStatus Status, string? ResponseNote, bool IsLate, bool CanRespond, string Attachments);

public sealed record FollowUpDocumentDto(Guid Id, Guid DocumentId, FollowUpDocumentKind Kind, string Title, int Version, FollowUpDocumentStatus Status,
    string UploadedBy, DateTimeOffset UploadedAtUtc, string? StageName, string? ReviewedBy, string? ReviewNote, string FileName);

public sealed record FollowUpApprovalDto(Guid Id, string? StageName, string RequestedBy, string Approver, Guid ApproverUserId, string ApproverRole,
    IReadOnlyList<string> ReviewItems, IReadOnlyList<string> PassedItems, DateTimeOffset RequestedAtUtc, ApprovalDecision? Decision, string? DecisionNote,
    string? CorrectionOwner, DateTimeOffset? CorrectionDueAtUtc, string? DecidedBy, DateTimeOffset? DecidedAtUtc, bool CanDecide);

public sealed record FollowUpActivityDto(Guid Id, ActivityType Type, string Subject, string? Description, string Owner, string? Contact, FollowUpChannel Channel,
    DateTimeOffset AtUtc, ActivityStatus Status, bool IsOverdue, string? Stage, string? ResultCode, string? Outcome, bool CanRecordResult, long Version,
    IReadOnlyList<string> PreChecklist);

public sealed record FollowUpEventDto(string Kind, string Title, string? Detail, string? Actor, DateTimeOffset AtUtc);

public sealed record FollowUpCloseCheckDto(string Label, bool Passed, string? Hint);

public sealed record FollowUpRuleDto(int Index, string Condition, string Action);

public sealed record FollowUpCaseDto(
    Guid Id,
    string Code,
    string Subject,
    Guid CustomerId,
    string CustomerName,
    string? Contact,
    string CaseType,
    string TemplateName,
    int TemplateVersion,
    FollowUpPriority Priority,
    string? PriorityReason,
    FollowUpChannel Channel,
    FollowUpStatus Status,
    int Progress,
    string BranchId,
    string BranchName,
    Guid? OwnerUserId,
    string? Owner,
    string? Queue,
    string? Description,
    string? ExpectedOutcome,
    IReadOnlyList<(string Label, string Value)> ExtraFields,
    FollowUpRelatedKind RelatedKind,
    string? RelatedLabel,
    string? RelatedUrl,
    string? Dealer,
    string? NextAction,
    DateTimeOffset? NextActionAtUtc,
    string? NextActionOwner,
    DateTimeOffset? FirstResponseDueAtUtc,
    DateTimeOffset? FirstRespondedAtUtc,
    DateTimeOffset? ResolutionDueAtUtc,
    DateTimeOffset? NearestDueUtc,
    bool IsOverdue,
    bool IsPaused,
    int PausedMinutes,
    int ElapsedWorkingMinutes,
    string? WaitReason,
    string? WaitingOn,
    DateTimeOffset? ReviewAtUtc,
    string? Outcome,
    string? OutcomeNote,
    DateTimeOffset? ClosedAtUtc,
    int ReopenCount,
    string? CancelReason,
    Guid? MergedIntoCaseId,
    Guid? ParentCaseId,
    DateTimeOffset OpenedAtUtc,
    string SlaPolicy,
    IReadOnlyList<FollowUpStageDto> Stages,
    IReadOnlyList<FollowUpPartDto> Items,
    IReadOnlyList<FollowUpActivityDto> Activities,
    IReadOnlyList<FollowUpReferralDto> Referrals,
    IReadOnlyList<FollowUpDocumentDto> Documents,
    IReadOnlyList<FollowUpApprovalDto> Approvals,
    IReadOnlyList<FollowUpEventDto> History,
    IReadOnlyList<FollowUpCloseCheckDto> CloseChecks,
    IReadOnlyList<FollowUpRuleDto> Rules,
    IReadOnlyList<FollowUpOptionDto> Users,
    long Version)
{
    public bool CanUpdate { get; init; }
    public bool CanAssign { get; init; }
    public bool CanApprove { get; init; }
    public bool CanClose { get; init; }
    public bool CanReopen { get; init; }
    public bool CanSupervise { get; init; }
    /// <summary>Users who may decide approvals (FollowUp.Approve) in the case's company.</summary>
    public IReadOnlyList<FollowUpOptionDto> Approvers { get; init; } = [];
    public IReadOnlyList<FollowUpOptionDto> Branches { get; init; } = [];
    public IReadOnlyList<FollowUpOptionDto> Contacts { get; init; } = [];
    public IReadOnlyDictionary<string, string?> ContactPhones { get; init; } = new Dictionary<string, string?>();
    /// <summary>Teams (queues) a referral can be addressed to.</summary>
    public IReadOnlyList<FollowUpOptionDto> Teams { get; init; } = [];
    /// <summary>Published templates offered when the conversation continues in a new case after closing.</summary>
    public IReadOnlyList<FollowUpOptionDto> NextTemplates { get; init; } = [];
    public bool PausesOnCustomer { get; init; } = true;
    public bool PausesOnInternal { get; init; }
    public bool IsOpen => Status is not (FollowUpStatus.Closed or FollowUpStatus.Cancelled);
    public bool IsWaiting => Status is FollowUpStatus.WaitingCustomer or FollowUpStatus.WaitingInternal or FollowUpStatus.OnHold;
    public FollowUpStageDto? ActiveStage => Stages.FirstOrDefault(x => x.Status is FollowUpStageStatus.Active or FollowUpStageStatus.Returned or FollowUpStageStatus.Waiting);
}

// ───────────── Actions ─────────────

/// <summary>۲. برنامه‌ریزی اقدام — a call, meeting, visit or internal task linked to the case and a stage.</summary>
public sealed record PlanFollowUpActionCommand(string Kind, string? Title, Guid? StageId, Guid OwnerUserId, Guid? ContactId, FollowUpChannel Channel,
    string? Date, string? Time, int? DurationMinutes, int? ReminderMinutes, ActivityPriority Priority, string? Instructions, string? Location,
    IReadOnlyList<string>? PreChecklist, IReadOnlyList<string>? PreChecklistDone, Guid OperationId, string? Phone = null, string? TimeZone = null);

/// <summary>۳. ثبت نتیجه — the coded result, the outcome text and the next action (required unless the case is being closed).</summary>
public sealed record RecordFollowUpResultCommand(string? ResultCode, string? Outcome, CallResult? CallResult, string? NextTitle, string? NextKind,
    Guid? NextOwnerUserId, string? NextDate, string? NextTime, bool RequestStageCompletion, long ExpectedVersion, string? DoneDate = null, string? DoneTime = null,
    Guid? DoneByUserId = null);

/// <summary>۴. ارجاع — stage or whole case, receiver, reason, acceptance deadline and what goes with it.</summary>
public sealed record ReferFollowUpCommand(ReferralScope Scope, Guid? StageId, Guid ToUserId, string? ToBranchId, string? ToTeam, string? Reason,
    string? AcceptDate, string? AcceptTime, bool IncludeHistory, bool IncludeQuote, bool IncludeTechnical);

/// <summary>۶. ثبت انتظار.</summary>
public sealed record WaitFollowUpCommand(FollowUpStatus Status, Guid? StageId, string? Reason, string? WaitingOn, string? RequestedDate,
    string? ExpectedDate, string? ExpectedTime, string? ReviewDate, string? ReviewTime, Guid ReviewOwnerUserId);

public sealed record ResumeFollowUpCommand(string? Note, string? NextTitle, string? NextDate, string? NextTime);

/// <summary>۷. درخواست تأیید و تصمیم.</summary>
public sealed record RequestFollowUpApprovalCommand(Guid? StageId, Guid ApproverUserId, string? ApproverRole, IReadOnlyList<string>? ReviewItems);
public sealed record DecideFollowUpApprovalCommand(ApprovalDecision Decision, string? Note, IReadOnlyList<string>? PassedItems, Guid? CorrectionOwnerUserId,
    string? CorrectionDate, string? CorrectionTime);

public sealed record FollowUpDocumentUpload(string FileName, string ContentType, byte[] Content);

/// <summary>۸. بستن و بازگشایی.</summary>
public sealed record CloseFollowUpCommand(string? Outcome, string? Note, bool StartNextCase, Guid? NextTemplateId, Guid? NextOwnerUserId);
public sealed record ReopenFollowUpCommand(string? Reason, Guid OwnerUserId, string? DueDate, string? DueTime);

public sealed record FollowUpActionResult(Guid CaseId, string Message, Guid? NewCaseId = null, bool Replayed = false);

// ───────────── Configuration (۹، ۱۰، ۱۱) ─────────────

public sealed record FollowUpTemplateStageInput(string? Name, int Weight, string? ResponsibleRole, bool Required, string? Checklist, string? Condition,
    bool ParallelWithPrevious, int? DurationHours);

public sealed record SaveFollowUpTemplateCommand(string? Name, string? CaseType, string? Scope, string? OwnerUnit, string? Description, string? ClosingCriteria,
    bool RequireAllRequiredStages, bool RequireCustomerApproval, string? Rules, Guid? SlaPolicyId, FollowUpPriority DefaultPriority,
    IReadOnlyList<FollowUpTemplateStageInput> Stages, long ExpectedVersion);

public sealed record FollowUpTemplateSummaryDto(Guid Id, string Code, string Name, string CaseType, int Version, FollowUpTemplateStatus Status,
    int StageCount, int OpenCases, DateTimeOffset? PublishedAtUtc, bool HasDraft);

public sealed record FollowUpTemplateDto(Guid Id, string Code, string Name, string CaseType, int Version, FollowUpTemplateStatus Status, string Scope,
    string OwnerUnit, string? Description, string? ClosingCriteria, bool RequireAllRequiredStages, bool RequireCustomerApproval, string? Rules,
    Guid? SlaPolicyId, FollowUpPriority DefaultPriority, IReadOnlyList<FollowUpTemplateStageInput> Stages, int CurrentPublishedVersion,
    IReadOnlyList<FollowUpOptionDto> Policies, IReadOnlyList<FollowUpOptionDto> Branches, long EntityVersion);

public sealed record SaveSlaPolicyCommand(string? Name, FollowUpPriority Priority, string? CaseType, string? TimeZoneId, IReadOnlyList<DayOfWeek>? WorkDays,
    string? WorkStart, string? WorkEnd, string? Holidays, int FirstResponseHours, int StageHours, int ResolutionHours, bool PauseOnWaitingCustomer,
    bool PauseOnWaitingInternal, IReadOnlyList<SlaEscalation>? Escalations, bool IsActive, long ExpectedVersion);

public sealed record SlaPolicyDto(Guid Id, string Name, FollowUpPriority Priority, string? CaseType, string TimeZoneId, IReadOnlyList<DayOfWeek> WorkDays,
    string WorkStart, string WorkEnd, string? Holidays, int FirstResponseHours, int StageHours, int ResolutionHours, bool PauseOnWaitingCustomer,
    bool PauseOnWaitingInternal, IReadOnlyList<SlaEscalation> Escalations, bool IsActive, int UsedByTemplates, long Version);

public sealed record SlaPreviewDto(string Start, string FirstResponseDue, string StageDue, string ResolutionDue, IReadOnlyList<string> EscalationTimes);

public sealed record SaveFollowUpQueueCommand(string? Name, string? BranchId, string? CaseType, string? PartFamily, string? Language, AssignmentMethod Method,
    QueueOrdering Ordering, Guid? OverflowQueueId, bool NotifySupervisorOnOverflow, int RuleOrder, bool IsActive,
    IReadOnlyList<(Guid UserId, int Capacity, bool Available, string? Note)> Members, long ExpectedVersion);

public sealed record FollowUpQueueMemberDto(Guid UserId, string Name, int Capacity, int Load, bool Available, string? Note);

public sealed record FollowUpQueueDto(Guid Id, string Name, string Country, string? BranchId, string? CaseType, string? PartFamily, string? Language,
    AssignmentMethod Method, QueueOrdering Ordering, Guid? OverflowQueueId, bool NotifySupervisorOnOverflow, int RuleOrder, bool IsActive,
    IReadOnlyList<FollowUpQueueMemberDto> Members, int WaitingCases, long Version);

public sealed record AssignmentCandidateDto(Guid UserId, string Name, int Capacity, int Load, bool Available, string? Note, bool Eligible);

public sealed record AssignmentSuggestionDto(Guid? QueueId, string? QueueName, Guid? UserId, string? UserName, string Explanation,
    IReadOnlyList<AssignmentCandidateDto> Candidates, bool Overflowed);

public sealed record FollowUpConfigurationDto(IReadOnlyList<FollowUpTemplateSummaryDto> Templates, IReadOnlyList<SlaPolicyDto> Policies,
    IReadOnlyList<FollowUpQueueDto> Queues, IReadOnlyList<FollowUpOptionDto> Users, IReadOnlyList<FollowUpOptionDto> Branches);

// ───────────── Supervision (۱۲. نظارت مدیر) ─────────────

public sealed record FollowUpSupervisionQuery(string? BranchId = null, Guid? QueueId = null, string? Period = null);

public sealed record FollowUpAttentionDto(Guid Id, string Code, string CustomerName, string Subject, string? Owner, string Reason, string Tone,
    DateTimeOffset? DueUtc, int Severity)
{
    /// <summary>unassigned, nonext, overdue, stage, near, referral, rejected, review.</summary>
    public string Kind { get; init; } = "overdue";
}

public sealed record FollowUpStageWaitDto(string Stage, double AverageHours, int Count);

public sealed record FollowUpOwnerLoadDto(string Owner, int Open, int Overdue, int? Capacity);

public sealed record FollowUpSupervisionDto(
    FollowUpSupervisionQuery Query,
    int OpenCount,
    int OverdueCount,
    int NoNextActionCount,
    int RejectedReferrals,
    int PendingReferrals,
    int ClosedInPeriod,
    int SlaMetPercent,
    int ReopenRatePercent,
    IReadOnlyList<FollowUpAttentionDto> Attention,
    IReadOnlyList<FollowUpStageWaitDto> LongestWaits,
    IReadOnlyList<FollowUpOwnerLoadDto> Load,
    IReadOnlyList<FollowUpOptionDto> Branches,
    IReadOnlyList<FollowUpOptionDto> Queues,
    IReadOnlyList<(Guid Id, string Name, string Query)> SavedViews,
    DateTimeOffset GeneratedAtUtc);

public sealed record FollowUpMonitorResult(int Escalations, int Reminders, int LateReferrals, int AutoAssigned);
