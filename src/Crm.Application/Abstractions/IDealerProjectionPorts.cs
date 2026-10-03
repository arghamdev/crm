namespace Crm.Application.Abstractions;

public sealed record DealerFinancialProjection(
    decimal CreditLimit,
    decimal CreditUsed,
    decimal Balance,
    decimal OverdueAmount,
    string Source,
    DateTimeOffset SynchronizedAtUtc);

public sealed record DealerPerformanceProjection(
    DateTimeOffset PeriodFromUtc,
    DateTimeOffset PeriodToUtc,
    decimal NetSales,
    int OrderCount,
    string Source,
    DateTimeOffset SynchronizedAtUtc);

public interface IDealerFinancialProjectionProvider
{
    DealerFinancialProjection Get(string companyId, string dealerBusinessId, DateTimeOffset nowUtc);
}

public interface IDealerPerformanceProjectionProvider
{
    DealerPerformanceProjection Get(string companyId, string dealerBusinessId, DateTimeOffset nowUtc);
}
