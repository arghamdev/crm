using Crm.Application.Abstractions;

namespace Crm.Infrastructure.Channel;

public sealed class DemoDealerFinancialProjectionProvider : IDealerFinancialProjectionProvider
{
    public DealerFinancialProjection Get(string companyId, string dealerBusinessId, DateTimeOffset nowUtc)
    {
        var pilot = dealerBusinessId.Equals("P-D01", StringComparison.OrdinalIgnoreCase);
        return new DealerFinancialProjection(
            pilot ? 12_000_000_000m : 5_000_000_000m,
            pilot ? 4_350_000_000m : 1_250_000_000m,
            pilot ? 3_980_000_000m : 980_000_000m,
            pilot ? 620_000_000m : 0,
            "Accounting Mock / Dealer Ledger v1",
            nowUtc);
    }
}

public sealed class DemoDealerPerformanceProjectionProvider : IDealerPerformanceProjectionProvider
{
    public DealerPerformanceProjection Get(string companyId, string dealerBusinessId, DateTimeOffset nowUtc)
    {
        var start = new DateTimeOffset(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var pilot = dealerBusinessId.Equals("P-D01", StringComparison.OrdinalIgnoreCase);
        return new DealerPerformanceProjection(start, start.AddMonths(1),
            pilot ? 6_850_000_000m : 1_400_000_000m,
            pilot ? 18 : 4,
            "ERP/BI Mock / Dealer Sales v1",
            nowUtc);
    }
}
