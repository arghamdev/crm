using Crm.Application.Abstractions;

namespace Crm.Infrastructure.Reporting;

public sealed class DemoReportingFinanceSource : IReportingFinanceSource
{
    public IReadOnlyList<ReceivableFact> Read(string companyId, IReadOnlySet<Guid> allowedCustomerIds, DateTimeOffset nowUtc)
    {
        var month = new DateTimeOffset(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var rows = new[]
        {
            new ReceivableFact("DEMO-INV-901", Guid.Parse("20000000-0000-4000-8000-000000000001"), "C01", "B01", "T01", month, month.AddDays(10), 1200000000m, 850000000m, "IRR", nowUtc.AddMinutes(-3), "Accounting Demo v1"),
            new ReceivableFact("DEMO-INV-902", Guid.Parse("20000000-0000-4000-8000-000000000002"), "C01", "B02", "T02", month, month.AddDays(15), 700000000m, 400000000m, "IRR", nowUtc.AddMinutes(-24), "Accounting Demo v1")
        };
        return rows.Where(x => x.CompanyId == companyId && allowedCustomerIds.Contains(x.CustomerId)).ToArray();
    }
}
