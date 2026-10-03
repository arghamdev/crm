using Crm.Domain.Commercial;

namespace Crm.Application.Contracts;

public sealed record ProductPriceDto(string Code, string Name, string Unit, string CurrencyCode,
    decimal ListUnitPrice, decimal StandardUnitCost, string Source, DateTimeOffset EffectiveAtUtc);

public sealed record QuoteLineDto(Guid Id, string ProductCode, string ProductName, string Unit, decimal Quantity,
    decimal ListUnitPrice, decimal DiscountPercent, decimal GrossAmount, decimal DiscountAmount,
    decimal NetAmount, decimal MarginPercent, string PriceSource, DateTimeOffset PriceEffectiveAtUtc, bool MarginMasked = false);

public sealed record QuoteApprovalDecisionDto(Guid Id, QuoteApprovalRole Role, QuoteDecision Decision,
    string Comment, Guid DecidedByUserId, string DecidedBy, DateTimeOffset DecidedAtUtc);

public sealed record QuoteStatusHistoryDto(Guid Id, QuoteStatus? FromStatus, QuoteStatus ToStatus,
    string Reason, Guid ChangedByUserId, string ChangedBy, DateTimeOffset ChangedAtUtc);

public sealed record QuoteSummaryDto(Guid Id, string Code, int Revision, string Customer, string Opportunity,
    Guid CustomerId, Guid? OpportunityId, Guid? OwnerUserId, string CompanyId, string BranchId, string? TerritoryId,
    string CurrencyCode, DateTimeOffset ValidUntilUtc, decimal GrossAmount, decimal DiscountAmount, decimal NetAmount,
    decimal DiscountPercent, decimal MarginPercent, QuoteApprovalLevel ApprovalLevel, QuoteStatus Status, long Version,
    bool MarginMasked = false);

public sealed record QuoteDetailsDto(QuoteSummaryDto Quote, IReadOnlyList<QuoteLineDto> Lines,
    IReadOnlyList<QuoteApprovalDecisionDto> Decisions, IReadOnlyList<QuoteStatusHistoryDto> History,
    IReadOnlyList<QuoteApprovalRole> PendingRoles, bool CanEdit, bool CanSubmit, bool CanApprove,
    bool CanSend, bool CanRecordOutcome, bool CanRevise);

public sealed record QuoteWorkspaceDto(IReadOnlyList<QuoteSummaryDto> Items, decimal OpenNetAmount,
    int DraftCount, int PendingApprovalCount, int ExpiringCount);

public sealed record CreateQuoteDraftCommand(Guid CustomerId, Guid OpportunityId, string BranchId,
    string CurrencyCode, DateTimeOffset ValidUntilUtc, string PaymentTerms, string ProductCode,
    decimal Quantity, decimal DiscountPercent);

public sealed record AddQuoteLineCommand(string ProductCode, decimal Quantity, decimal DiscountPercent, long ExpectedVersion);
public sealed record SubmitQuoteCommand(string Reason, long ExpectedVersion);
public sealed record DecideQuoteCommand(QuoteDecision Decision, string Comment, long ExpectedVersion);
public sealed record QuoteOutcomeCommand(bool Accepted, string Reason, long ExpectedVersion);
public sealed record ReviseQuoteCommand(DateTimeOffset ValidUntilUtc, string Reason, long ExpectedVersion);
