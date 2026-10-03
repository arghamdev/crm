namespace Crm.Application.Abstractions;

// Invoice-cohort snapshot, not a payment-date ledger. Production adapters must preserve this grain.
public sealed record ReceivableFact(string InvoiceId, Guid CustomerId, string CompanyId,
    string BranchId, string? TerritoryId, DateTimeOffset InvoiceAtUtc, DateTimeOffset DueAtUtc,
    decimal InvoicedAmount, decimal CollectedAmount, string Currency,
    DateTimeOffset SynchronizedAtUtc, string Source);

public interface IReportingFinanceSource
{
    IReadOnlyList<ReceivableFact> Read(string companyId, IReadOnlySet<Guid> allowedCustomerIds,
        DateTimeOffset nowUtc);
}
