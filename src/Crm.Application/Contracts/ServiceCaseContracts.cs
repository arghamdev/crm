using Crm.Domain.Service;

namespace Crm.Application.Contracts;

public sealed record ServiceCaseDto(
    Guid Id,
    string Code,
    string Subject,
    string Description,
    Guid CustomerId,
    string CustomerName,
    string CompanyId,
    string BranchId,
    string? TerritoryId,
    ServiceCaseCategory Category,
    ServiceCaseChannel Channel,
    ServiceCasePriority Priority,
    ServiceCaseStatus Status,
    Guid? OwnerUserId,
    string Owner,
    DateTimeOffset OpenedAtUtc,
    DateTimeOffset FirstResponseDueAtUtc,
    DateTimeOffset ResolutionDueAtUtc,
    DateTimeOffset? FirstRespondedAtUtc,
    DateTimeOffset? ResolvedAtUtc,
    DateTimeOffset? ClosedAtUtc,
    long PausedMinutes,
    int EscalationLevel,
    string? RootCause,
    string? CorrectiveAction,
    string? Resolution,
    int ReopenCount,
    int? SatisfactionScore,
    string? SatisfactionComment,
    ServiceSlaState FirstResponseSla,
    ServiceSlaState ResolutionSla,
    long Version);

public sealed record ServiceCaseHistoryDto(
    Guid Id,
    ServiceCaseStatus? FromStatus,
    ServiceCaseStatus ToStatus,
    string Action,
    string Note,
    string ActorName,
    DateTimeOffset OccurredAtUtc);

public sealed record ServiceCaseListDto(
    IReadOnlyList<ServiceCaseDto> Items,
    string? Query,
    ServiceCaseStatus? Status,
    ServiceCasePriority? Priority,
    bool IncludeClosed,
    bool OnlyMine,
    int OpenCount,
    int BreachedCount,
    int AtRiskCount,
    int EscalatedCount,
    decimal? AverageSatisfaction,
    decimal? ResolutionSlaComplianceRate,
    bool CanCreate,
    bool CanTriage,
    int Page = 1,
    int PageSize = Crm.Application.Abstractions.PageRequest.DefaultPageSize,
    int TotalCount = 0)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)Math.Max(1, PageSize)));
}

public sealed record ServiceCaseDetailsDto(
    ServiceCaseDto Case,
    IReadOnlyList<ServiceCaseHistoryDto> History,
    IReadOnlyList<SalesOwnerOptionDto> EligibleOwners,
    bool CanTriage,
    bool CanWork,
    bool CanClose,
    bool CanReopen);

public sealed record ServiceCaseCustomerOptionDto(Guid Id, string Code, string Name, string BranchId);

public sealed record CreateServiceCaseCommand(
    Guid CustomerId,
    string Subject,
    string? Description,
    ServiceCaseCategory Category,
    ServiceCaseChannel Channel,
    ServiceCasePriority Priority);

public sealed record TriageServiceCaseCommand(ServiceCasePriority Priority, Guid OwnerUserId, string? Note, long ExpectedVersion);

public enum ServiceCaseAction { StartWork, WaitOnCustomer, Resolve, Close, Reopen }

public sealed record ServiceCaseActionCommand(
    ServiceCaseAction Action,
    string? Note,
    string? RootCause,
    string? CorrectiveAction,
    string? Resolution,
    int? SatisfactionScore,
    long ExpectedVersion);

public sealed record ServiceEscalationResult(int EscalatedCases, int WorkItemsCreated);
