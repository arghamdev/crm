using Crm.Domain.SelfService;

namespace Crm.Application.Contracts;

public sealed record PortalProductDto(string Code, string Name, string Unit, decimal UnitPrice, int? Available,
    string Source, DateTimeOffset SynchronizedAtUtc);
public sealed record PortalInvoiceDto(string Id, string Number, decimal Total, decimal Collected,
    DateTimeOffset DueAtUtc, string Source, DateTimeOffset SynchronizedAtUtc);
public sealed record PortalAccountDto(Guid Id, string Name, string UserName, string Status);
public sealed record PortalRequestDto(Guid Id, string Code, Guid DealerId, string DealerName, PortalRequestKind Kind,
    PortalRequestStatus Status, string Subject, string Description, string PublicReply, string? ProductCode,
    decimal Quantity, decimal UnitPrice, string? Source, DateTimeOffset? PriceAtUtc, long Version,
    DateTimeOffset CreatedAtUtc, Guid? LinkedRecordId, DateTimeOffset? ProtectedUntilUtc, string? OrderStatus,
    Guid CreatedByUserId, Guid? CustomerId, string? Email, Guid? TargetUserId,
    string? ServiceCaseCode = null, string? CustomerName = null);
public sealed record PortalDashboardDto(Guid DealerId, string DealerName, bool CanSubmit,
    decimal? Balance, decimal? Overdue, string? FinanceSource, DateTimeOffset? FinanceAtUtc,
    decimal? Target, decimal? Actual, IReadOnlyList<DealerCustomerOptionDto> Customers,
    IReadOnlyList<PortalProductDto> Products, IReadOnlyList<PortalInvoiceDto> Invoices,
    IReadOnlyList<PortalAccountDto> Accounts, IReadOnlyList<PortalRequestDto> Requests);
public sealed record SubmitPortalRequestCommand(Guid OperationId, PortalRequestKind Kind, string Subject,
    string Description, Guid? CustomerId = null, string? ProductCode = null, decimal Quantity = 0,
    string? Email = null, Guid? TargetUserId = null);
public sealed record ReviewPortalRequestCommand(long ExpectedVersion, PortalRequestStatus Status,
    string Reply, Guid? LinkedOrderId = null, Guid? CustomerId = null);
public sealed record PortalRequestFormDto(PortalDashboardDto Dashboard, SubmitPortalRequestCommand Command);
public sealed record PortalReviewDto(PortalRequestDto Request, bool CanAcceptLead, bool CanManageAccounts,
    IReadOnlyList<DealerCustomerOptionDto>? DealerCustomers = null);
public sealed record CreateVisitCommand(Guid OperationId, Guid CustomerId, DateTime PlannedAtUtc, string Purpose);
public sealed record VisitActionCommand(Guid OperationId, long ExpectedVersion, VisitStatus Status,
    string Outcome, DateTimeOffset? OccurredAtUtc = null, bool LocationConsent = false,
    decimal? Latitude = null, decimal? Longitude = null);
public sealed record MobileVisitDto(Guid Id, Guid CustomerId, string CustomerName, string BranchId,
    DateTimeOffset PlannedAtUtc, string Purpose, VisitStatus Status, long Version, string Outcome,
    DateTimeOffset? CheckedInAtUtc, DateTimeOffset? CompletedAtUtc, DateTimeOffset? LastReceivedAtUtc, bool HasLocation);
public sealed record MobileWorkspaceDto(IReadOnlyList<MobileVisitDto> Visits,
    IReadOnlyList<DealerCustomerOptionDto> Customers, DateOnly Day);
public sealed record MobileAck(Guid VisitId, long AppliedVersion, bool Replayed, MobileVisitDto Visit);
public sealed class SelfServiceConflictException(string message) : InvalidOperationException(message);
