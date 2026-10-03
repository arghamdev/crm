using Crm.Domain.Commercial;
using Crm.Domain.Customers;
using Crm.Domain.Sales;

namespace Crm.Application.Contracts;

public sealed record DashboardDto(
    decimal PipelineValue,
    int OpenLeadCount,
    int PendingQuoteCount,
    int OpenTaskCount,
    IReadOnlyList<OpportunityDto> HotOpportunities,
    IReadOnlyList<WorkItemDto> TodayTasks);

public sealed record CustomerDto(Guid Id, string Code, string Name, string City, string Owner, string CompanyId, string Branch, string BranchId, string? TerritoryId, string Segment, CustomerStatus Status, decimal Balance, decimal CreditLimit,
    CustomerKind Kind = CustomerKind.Legal, string? NationalId = null, string? PrimaryPhone = null, string? PrimaryEmail = null,
    string DataSource = "CRM", DateTimeOffset? LastSynchronizedAtUtc = null, long Version = 1, int DataQualityScore = 0);
public sealed record LeadDto(Guid Id, string Code, string Name, string Contact, string Source, string Owner, string CompanyId, string BranchId, string? TerritoryId, int Score, LeadStatus Status, Guid? CustomerId = null,
    Guid? OwnerUserId = null, string? Phone = null, string? Email = null, DateTimeOffset? AssignedAtUtc = null,
    DateTimeOffset? FirstContactDueAtUtc = null, DateTimeOffset? FirstContactAtUtc = null, DateTimeOffset? LastActivityAtUtc = null,
    string? NextAction = null, DateTimeOffset? NextActionAtUtc = null, string? StatusReason = null,
    Guid? ConvertedOpportunityId = null, long Version = 1, LeadSlaState SlaState = LeadSlaState.NotApplicable);
public sealed record OpportunityDto(Guid Id, string Code, string Title, string Customer, decimal Value, string Owner, string CompanyId, string BranchId, string? TerritoryId, OpportunityStage Stage, int Probability, Guid CustomerId = default,
    Guid? OwnerUserId = null, Guid? OriginLeadId = null, DateTimeOffset? ExpectedCloseAtUtc = null, string Source = "Direct",
    string? NextAction = null, DateTimeOffset? NextActionAtUtc = null, DateTimeOffset? LastActivityAtUtc = null,
    string? Competitor = null, OpportunityRiskLevel RiskLevel = OpportunityRiskLevel.Medium,
    string? OutcomeReason = null, DateTimeOffset? ClosedAtUtc = null, long Version = 1);
public sealed record QuoteDto(Guid Id, string Code, string Customer, string Opportunity, decimal Amount, decimal DiscountPercent, decimal MarginPercent, string CompanyId, string BranchId, string? TerritoryId, QuoteStatus Status, decimal NetAmount, Guid CustomerId = default, Guid? OpportunityId = null);
public sealed record WorkItemDto(Guid Id, string Title, string Priority, DateTimeOffset DueAtUtc, bool IsDone);
public sealed record CreateCustomerCommand(string Name, string City, string Owner, string BranchId, string Segment,
    CustomerKind Kind = CustomerKind.Legal, string? NationalId = null, string? PrimaryPhone = null, string? PrimaryEmail = null,
    bool AllowPotentialDuplicate = false, string? DuplicateReason = null);
public sealed record CreateLeadCommand(string Name, string Contact, string Source, string Owner, string BranchId,
    Guid? OwnerUserId = null, string? Phone = null, string? Email = null, string? TerritoryId = null,
    string? NextAction = null, DateTimeOffset? NextActionAtUtc = null);
public sealed record CreateQuoteCommand(string? Customer, string? Opportunity, decimal Amount, decimal DiscountPercent, decimal MarginPercent, string BranchId,
    Guid CustomerId = default, Guid? OpportunityId = null);
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount, string? Query = null)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;
}
