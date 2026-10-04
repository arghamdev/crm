using Crm.Domain.Accounts;
using Crm.Domain.Customers;

namespace Crm.Application.Contracts;

/// <summary>Amount in one currency; amounts in different currencies are never added together.</summary>
public sealed record MoneyDto(decimal Amount, string Currency);

public sealed record AccountKpiDto(string Key, string Label, string Value, string? Detail, string Definition, string? Url = null, string Tone = "neutral");

public sealed record AccountSummaryDto(
    Guid Id,
    string Code,
    string Name,
    CustomerKind Kind,
    CustomerStatus Status,
    AccountRelationship Relationship,
    IReadOnlyList<string> Tags,
    string Owner,
    string CompanyId,
    string CompanyName,
    string BranchName,
    string? TerritoryId,
    string Segment,
    bool HasLogo,
    long Version,
    Guid? ParentId,
    string? ParentName,
    IReadOnlyList<(string Label, string Value, bool Ltr)> Phones,
    string? Email,
    string? Website,
    string? Address,
    string? Industry,
    IReadOnlyList<(string Label, string Value)> Identifiers,
    bool Masked);

/// <summary>Navigation entry of a related-records section (sections the user may not read are left out).</summary>
public sealed record AccountSectionInfo(string Key, string Title, string Group, string Icon, int Count, bool CanCreate, bool CanLink);

public sealed record AccountQuickActions(bool Opportunity, bool Call, bool Meeting, bool Task, bool Note, bool Lead);

public sealed record AccountFileDto(
    AccountSummaryDto Account,
    IReadOnlyList<AccountKpiDto> Kpis,
    AccountQuickActions Quick,
    IReadOnlyList<AccountSectionInfo> Sections,
    bool CanEdit,
    bool CanChangeStatus,
    bool CanReadActivities,
    bool CanReadNotes,
    IReadOnlyList<DataQualityIssueDto> DataQualityIssues);

public sealed record AccountRowAction(string Label, string Path, IReadOnlyDictionary<string, string>? Fields = null, bool Danger = false,
    string? Confirm = null, bool Drawer = false, string? Prompt = null);

public sealed record AccountRowDto(
    Guid Id,
    string Title,
    string? Subtitle,
    string? Status,
    string Tone,
    MoneyDto? Amount,
    string? Date,
    string? Url,
    string? Owner,
    IReadOnlyList<AccountRowAction> Actions,
    IReadOnlyList<string>? Extra = null);

public sealed record AccountSectionDto(
    string Key,
    string Title,
    int Total,
    IReadOnlyList<AccountRowDto> Rows,
    int Page,
    int PageSize,
    string? Query,
    string? Status,
    IReadOnlyList<(string Value, string Label)> StatusOptions,
    IReadOnlyList<MoneyDto> Totals,
    string? TotalsLabel,
    string? CreatePath,
    string? CreateLabel,
    string? LinkPath,
    string? LinkLabel,
    string EmptyText,
    string? Note,
    IReadOnlyList<string> Columns)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
}

public sealed record ActivityFilter(ActivityType? Type = null, string? State = null, Guid? OwnerUserId = null, DateOnly? From = null,
    DateOnly? To = null, Guid? ContactId = null);

public sealed record ActivityItemDto(
    Guid Id,
    string Kind,
    ActivityType? Type,
    string Subject,
    string? Description,
    string Owner,
    Guid? OwnerUserId,
    string? Contact,
    string When,
    DateTimeOffset SortAtUtc,
    string State,
    bool IsOverdue,
    string? Related,
    string? RelatedUrl,
    string? Outcome,
    string? Details,
    bool CanEdit,
    bool CanComplete,
    long Version);

public sealed record ActivityPanelDto(
    IReadOnlyList<ActivityItemDto> Overdue,
    IReadOnlyList<ActivityItemDto> Upcoming,
    IReadOnlyList<ActivityItemDto> Past,
    int Total,
    ActivityFilter Filter,
    IReadOnlyList<UserOptionDto> Owners);

public sealed record TimelineItemDto(string Kind, string Title, string? Description, string When, DateTimeOffset AtUtc, string? Actor, string? Url, string Icon);

/// <summary>Picker options for quick-action forms, all restricted to the current account and the user's scope.</summary>
public sealed record AccountFormOptions(
    Guid AccountId,
    string AccountName,
    IReadOnlyList<(Guid Id, string Name)> Contacts,
    IReadOnlyList<UserOptionDto> Users,
    IReadOnlyList<(Guid Id, string Name)> Opportunities,
    IReadOnlyList<(Guid Id, string Name)> Projects,
    IReadOnlyList<(Guid Id, string Name)> Contracts,
    Guid CurrentUserId,
    bool CanAssignOthers);

/// <summary>Call / meeting / task form. Dates are Jalali (1405/07/12) and times HH:mm in Tehran time.</summary>
public sealed record SaveActivityCommand(
    ActivityType Type,
    string? Subject,
    string? Description,
    Guid? ContactId,
    Guid OwnerUserId,
    string? StartDate,
    string? StartTime,
    string? EndDate,
    string? EndTime,
    int? DurationMinutes,
    CallDirection? Direction,
    string? Location,
    ActivityPriority Priority,
    int? ReminderMinutesBefore,
    ActivityRelatedKind RelatedKind,
    Guid? RelatedId,
    IReadOnlyList<Guid>? ParticipantContactIds,
    IReadOnlyList<Guid>? ParticipantUserIds,
    Guid OperationId,
    long ExpectedVersion = 0,
    bool AlreadyDone = false,
    string? Outcome = null,
    CallResult? CallResult = null);

public sealed record CompleteActivityCommand(
    string? Outcome,
    CallResult? CallResult,
    int? ActualDurationMinutes,
    ActivityType? NextType,
    string? NextSubject,
    string? NextDate,
    string? NextTime,
    long ExpectedVersion);

public sealed record CancelActivityCommand(string? Reason, long ExpectedVersion);

public sealed record RescheduleActivityCommand(string? StartDate, string? StartTime, string? EndDate, string? EndTime, long ExpectedVersion);

public sealed record ActivityFormDto(Guid AccountId, Guid? ActivityId, SaveActivityCommand Command, AccountFormOptions Options);

public sealed record ActivityDetailsDto(ActivityItemDto Item, CrmActivitySnapshot Activity, IReadOnlyList<string> Participants, Guid AccountId);

public sealed record CrmActivitySnapshot(ActivityType Type, ActivityStatus Status, string StartDate, string StartTime, string? EndDate, string? EndTime,
    int? DurationMinutes, CallDirection? Direction, string? Location, ActivityPriority Priority, int? Reminder, CallResult? CallResult,
    string? CompletedBy, string? CompletedAt, string? CancelReason, string? FollowUpOf);

public sealed record SaveNoteCommand(string? Title, string? Body, NoteVisibility Visibility, Guid OperationId);

public sealed record NoteDto(Guid Id, string Title, string Body, NoteVisibility Visibility, string Author, string CreatedAt, bool CanEdit,
    IReadOnlyList<(Guid Id, string Name)> Attachments);

/// <summary>Uploaded file (bytes read by the web layer; type and size validated by the domain).</summary>
public sealed record FileUpload(string FileName, byte[] Content);

/// <summary>Generic form post for the account's related records; each section reads the fields it needs.</summary>
public sealed record AccountRecordCommand(IReadOnlyDictionary<string, string?> Fields, Guid OperationId, IReadOnlyList<FileUpload>? Files = null)
{
    public long? Version => long.TryParse(Get("expectedVersion"), out var value) ? value : null;

    public string? Get(string key) => Fields.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
}

public sealed record AccountLinkOption(Guid Id, string Title, string? Subtitle);

/// <summary>
/// One input of a generic account form (drawer). Types: text, textarea, number, money, date (Jalali), time, select,
/// checkbox, file, hidden. Values are strings so a rejected form can be shown again exactly as posted.
/// </summary>
public sealed record AccountFormField(string Name, string Label, string Type, bool Required = false, string? Value = null,
    IReadOnlyList<(string Value, string Label)>? Options = null, string? Help = null, bool Ltr = false, bool Wide = false, string? Placeholder = null);

public sealed record AccountRecordFormDto(Guid AccountId, string AccountName, string Key, string Title, string SubmitLabel, string Action,
    IReadOnlyList<AccountFormField> Fields, bool Multipart = false, string? Note = null, Guid OperationId = default);

/// <summary>Picker for linking an existing record; options are restricted to records the user may link to this account.</summary>
public sealed record AccountLinkFormDto(Guid AccountId, string AccountName, string Key, string Title, string Action, string? Query,
    IReadOnlyList<AccountLinkOption> Options, IReadOnlyList<AccountFormField> Fields, string? Note = null);
