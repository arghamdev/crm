using Crm.Domain.Accounts;
using Crm.Domain.Customers;

namespace Crm.Application.Contracts;

/// <summary>
/// Customer/account list request. Views: all, mine, nofollowup (active, no planned activity), inactive,
/// followup (planned activity due by the end of today or overdue), incomplete (identifier/phone/email missing), duplicates.
/// </summary>
public sealed record CustomerListQuery(string? View = null, string? Query = null, string? BranchId = null, CustomerKind? Kind = null,
    AccountRelationship? Relationship = null, string? Segment = null, CustomerStatus? Status = null, string? Owner = null,
    string? Sort = null, int Page = 1, int PageSize = 10);

public sealed record CustomerListRowDto(CustomerDto Customer, AccountRelationship Relationship, string? NextActivity,
    ActivityType? NextActivityType, DateTimeOffset? NextActivityAtUtc, bool PendingDuplicate);

public sealed record CustomerListDto(IReadOnlyList<CustomerListRowDto> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)Math.Max(1, PageSize)));

    public string View { get; init; } = "all";
    public string? Query { get; init; }
    public string? BranchId { get; init; }
    public CustomerKind? Kind { get; init; }
    public AccountRelationship? Relationship { get; init; }
    public string? Segment { get; init; }
    public CustomerStatus? Status { get; init; }
    public string? Owner { get; init; }
    public string Sort { get; init; } = "recent";

    public int VisibleCount { get; init; }
    public int ActiveCount { get; init; }
    public int MineCount { get; init; }
    public int NoFollowUpCount { get; init; }
    public int InactiveCount { get; init; }
    public int FollowUpDueCount { get; init; }
    public int IncompleteCount { get; init; }
    public int DuplicateCount { get; init; }

    public IReadOnlyList<string> Segments { get; init; } = [];
    public IReadOnlyList<string> Owners { get; init; } = [];
    public bool CanCreate { get; init; }
    public bool CanPlanActivities { get; init; }
    public bool CanReadActivities { get; init; }
    public bool CanReviewDuplicates { get; init; }
}

public sealed record BulkAccountTaskCommand(IReadOnlyList<Guid> AccountIds, string Subject, string? DueDate, string? DueTime, ActivityPriority Priority);
