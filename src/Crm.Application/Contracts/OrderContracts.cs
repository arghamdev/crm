using Crm.Domain.Commercial;

namespace Crm.Application.Contracts;

public sealed record EligibleOrderQuoteDto(Guid Id, string Code, int Revision, string Customer,
    string Opportunity, decimal NetAmount, string CurrencyCode, long Version);

public sealed record OrderSummaryDto(Guid Id, string Code, string QuoteCode, Guid QuoteId, string Customer,
    Guid CustomerId, string CompanyId, string BranchId, string? TerritoryId, string CurrencyCode,
    decimal NetAmount, OrderRequestStatus Status, string? ErpOrderNumber, DateTimeOffset? LastSynchronizedAtUtc,
    long Version);

public sealed record OrderCreditDecisionDto(Guid Id, OrderCreditDecisionType Decision, decimal CreditLimit,
    decimal CreditUsed, decimal OverdueAmount, decimal AvailableCredit, string Reason, string Source,
    string DecidedBy, DateTimeOffset DecidedAtUtc, DateTimeOffset? ExpiresAtUtc);

public sealed record OrderStatusHistoryDto(Guid Id, OrderRequestStatus? FromStatus, OrderRequestStatus ToStatus,
    string Reason, string Source, string ChangedBy, DateTimeOffset ChangedAtUtc);

public sealed record OrderIntegrationAttemptDto(Guid Id, int AttemptNumber, ErpSubmissionOutcome Outcome,
    string Detail, string? ExternalReference, DateTimeOffset AttemptedAtUtc);

public sealed record OrderIntegrationMessageDto(Guid Id, Guid OrderRequestId, string MessageType, string IdempotencyKey,
    string CorrelationId, OrderIntegrationStatus Status, int AttemptCount, DateTimeOffset? NextAttemptAtUtc,
    string? LastError, string? ExternalReference, long Version);

public sealed record OrderDetailsDto(OrderSummaryDto Order, decimal CreditLimit, decimal CreditUsed,
    decimal OverdueAmount, decimal AvailableCredit, bool IsCreditHold, string? CreditReason,
    string? CreditSource, DateTimeOffset? CreditSnapshotAtUtc, DateTimeOffset? CreditOverrideExpiresAtUtc,
    string? DeliveryReference, string? InvoiceNumber, string? PaymentReference,
    OrderIntegrationMessageDto? Integration, IReadOnlyList<OrderCreditDecisionDto> CreditDecisions,
    IReadOnlyList<OrderStatusHistoryDto> History, IReadOnlyList<OrderIntegrationAttemptDto> Attempts,
    bool CanCheckCredit, bool CanOverrideCredit, bool CanSubmit, bool CanProcessIntegration,
    bool CanAdvanceProjection);

public sealed record OrderWorkspaceDto(IReadOnlyList<OrderSummaryDto> Items,
    IReadOnlyList<OrderIntegrationMessageDto> IntegrationErrors, decimal OpenAmount,
    int CreditHoldCount, int PendingIntegrationCount, int FailedIntegrationCount);

public sealed record CreateOrderRequestCommand(Guid QuoteId, long ExpectedQuoteVersion);
public sealed record CheckOrderCreditCommand(long ExpectedVersion);
public sealed record OverrideOrderCreditCommand(string Reason, DateTimeOffset ExpiresAtUtc, long ExpectedVersion);
public sealed record QueueOrderSubmissionCommand(long ExpectedVersion);
public sealed record ProcessOrderIntegrationCommand(ErpSubmissionOutcome SimulatedOutcome,
    long ExpectedOrderVersion, long ExpectedMessageVersion);
public sealed record AdvanceOrderProjectionCommand(OrderRequestStatus TargetStatus,
    string ExternalReference, long ExpectedVersion);
