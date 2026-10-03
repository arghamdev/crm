using Crm.Domain.Commercial;

namespace Crm.Application.Abstractions;

public sealed record AccountingCreditSnapshot(string ExternalCustomerId, decimal CreditLimit, decimal CreditUsed,
    decimal OverdueAmount, bool IsCreditHold, string HoldReason, string Source, DateTimeOffset SnapshotAtUtc);

public interface IAccountingCreditProvider
{
    AccountingCreditSnapshot Get(string companyId, Guid customerId, DateTimeOffset nowUtc);
}

public sealed record ErpOrderSubmission(Guid OrderRequestId, string OrderCode, string IdempotencyKey,
    string CorrelationId, Guid CustomerId, decimal NetAmount, string CurrencyCode,
    ErpSubmissionOutcome SimulatedOutcome);

public sealed record ErpOrderSubmissionResult(ErpSubmissionOutcome Outcome, string Detail,
    string? ExternalReference);

public interface IErpOrderGateway
{
    ErpOrderSubmissionResult Submit(ErpOrderSubmission submission, DateTimeOffset nowUtc);
}
