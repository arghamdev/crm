using Crm.Domain.Channel;
using Crm.Domain.Customers;

namespace Crm.Application.Contracts;

public sealed record DealerSummaryDto(
    Guid Id, string DealerId, string Code, string LegalName, string TradeName, string City,
    string CompanyId, string BranchId, string? TerritoryId, string TerritoryName, DealerStatus Status,
    int ActiveContracts, int ActiveTerritories, int CustomerCount, decimal? Balance, decimal? OverdueAmount,
    decimal TargetAmount, decimal NetSales, decimal AchievementPercent, DateTimeOffset? FinancialSynchronizedAtUtc,
    long Version);

public sealed record DealerWorkspaceDto(
    IReadOnlyList<DealerSummaryDto> Items, int ActiveCount, int PendingApprovalCount,
    int ExpiringContractCount, int StaleFinancialCount, decimal? TotalOverdueAmount,
    decimal TotalTarget, decimal TotalNetSales);

public sealed record DealerContractDto(
    Guid Id, string ContractNumber, DateTimeOffset ValidFromUtc, DateTimeOffset ValidToUtc,
    decimal AnnualTarget, string PaymentTerms, DealerContractStatus Status, Guid RequestedByUserId,
    string RequestedBy, Guid? ApprovedByUserId, string? ApprovedBy, DateTimeOffset? ApprovedAtUtc,
    string? DecisionReason, long Version);

public sealed record DealerTerritoryAssignmentDto(
    Guid Id, string TerritoryId, string TerritoryName, bool IsExclusive,
    DateTimeOffset ValidFromUtc, DateTimeOffset? ValidToUtc, DealerTerritoryStatus Status,
    Guid RequestedByUserId, string RequestedBy, Guid? ApprovedByUserId, string? ApprovedBy,
    string? Reason, long Version);

public sealed record DealerCustomerAssignmentDto(
    Guid Id, Guid CustomerId, string CustomerCode, string CustomerName, string BranchId,
    string? TerritoryId, CustomerStatus CustomerStatus, DateTimeOffset ValidFromUtc,
    DateTimeOffset? ValidToUtc, bool IsActive, decimal? Balance, Guid? EndedByUserId,
    string? EndedBy, string? EndReason, long Version);

public sealed record DealerFinancialSnapshotDto(
    Guid Id, decimal CreditLimit, decimal CreditUsed, decimal AvailableCredit, decimal Balance,
    decimal OverdueAmount, string Source, DateTimeOffset SynchronizedAtUtc, string FreshnessStatus);

public sealed record DealerPerformanceSnapshotDto(
    Guid Id, DateTimeOffset PeriodFromUtc, DateTimeOffset PeriodToUtc, decimal TargetAmount,
    decimal NetSales, int OrderCount, decimal AchievementPercent, string Source,
    DateTimeOffset SynchronizedAtUtc, string FreshnessStatus);

public sealed record DealerStatusHistoryDto(
    Guid Id, DealerStatus? FromStatus, DealerStatus ToStatus, string Reason,
    Guid ChangedByUserId, string ChangedBy, DateTimeOffset ChangedAtUtc);

public sealed record DealerDetailsDto(
    DealerSummaryDto Dealer,
    string? NationalId,
    string? Phone,
    string? Email,
    Guid ChannelManagerUserId,
    string ChannelManager,
    string? StatusReason,
    DealerFinancialSnapshotDto? Financial,
    DealerPerformanceSnapshotDto? Performance,
    IReadOnlyList<DealerContractDto> Contracts,
    IReadOnlyList<DealerTerritoryAssignmentDto> Territories,
    IReadOnlyList<DealerCustomerAssignmentDto> Customers,
    IReadOnlyList<DealerStatusHistoryDto> History,
    bool CanEdit,
    bool CanSubmit,
    bool CanApprove,
    bool CanSuspend,
    bool CanTerminate,
    bool CanRequestContract,
    bool CanApproveContract,
    bool CanRequestTerritory,
    bool CanApproveTerritory,
    bool CanAssignCustomer,
    bool CanSetTarget,
    bool CanSyncFinancial,
    bool CanSyncPerformance);

public sealed record DealerManagerOptionDto(Guid Id, string Name);
public sealed record DealerFormDto(
    Guid? Id, string DealerId, string Code, string LegalName, string TradeName, string BranchId,
    string? TerritoryId, string City, string? NationalId, string? Phone, string? Email,
    Guid ChannelManagerUserId, long ExpectedVersion,
    IReadOnlyList<OrganizationUnitOptionDto> Branches,
    IReadOnlyList<TerritoryOptionDto> Territories,
    IReadOnlyList<DealerManagerOptionDto> Managers);

public sealed record SaveDealerCommand(
    string DealerId, string Code, string LegalName, string TradeName, string BranchId,
    string? TerritoryId, string City, string? NationalId, string? Phone, string? Email,
    Guid ChannelManagerUserId, long ExpectedVersion);

public sealed record ChangeDealerStatusCommand(DealerStatus TargetStatus, string Reason, long ExpectedVersion);
public sealed record SaveDealerContractCommand(
    string ContractNumber, DateTimeOffset ValidFromUtc, DateTimeOffset ValidToUtc,
    decimal AnnualTarget, string PaymentTerms, long ExpectedVersion);
public sealed record DecideDealerContractCommand(string Reason, long ExpectedVersion);
public sealed record AssignDealerTerritoryCommand(
    string TerritoryId, bool IsExclusive, DateTimeOffset ValidFromUtc, DateTimeOffset? ValidToUtc);
public sealed record DecideDealerTerritoryCommand(string Reason, long ExpectedVersion);
public sealed record EndDealerRelationshipCommand(string Reason, long ExpectedVersion);
public sealed record SaveDealerTargetCommand(
    DateTimeOffset PeriodFromUtc, DateTimeOffset PeriodToUtc, decimal Amount, string Source,
    long ExpectedVersion);
public sealed record AssignDealerCustomerCommand(Guid CustomerId, string Reason);
public sealed record DealerContractFormDto(Guid DealerId, SaveDealerContractCommand Command);
public sealed record DealerTerritoryFormDto(Guid DealerId, AssignDealerTerritoryCommand Command,
    IReadOnlyList<TerritoryOptionDto> Options);
public sealed record DealerTargetFormDto(Guid DealerId, SaveDealerTargetCommand Command);
public sealed record DealerCustomerOptionDto(Guid Id, string Code, string Name, string BranchId);
public sealed record DealerCustomerFormDto(Guid DealerId, AssignDealerCustomerCommand Command,
    IReadOnlyList<DealerCustomerOptionDto> Options);
