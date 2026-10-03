using System.Text;
using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Application.Services;
using Crm.Domain.Channel;
using Crm.Domain.Identity;
using Crm.Domain.Sales;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Identity;
using Crm.Infrastructure.Reporting;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

internal static class ReportingChecks
{
    internal static void Run(Action<bool, string> check)
    {
        using var store = new InMemoryCrmDataStore();
        using var services = new ServiceCollection().AddDistributedMemoryCache().BuildServiceProvider();
        var access = new DemoAccessSnapshotService(store, services.GetRequiredService<IDistributedCache>());
        var reporting = new ReportingService(store, access, new DemoReportingFinanceSource());
        var manager = Guid.Parse("10000000-0000-4000-8000-000000000001");
        var expert = Guid.Parse("10000000-0000-4000-8000-000000000002");
        var finance = Guid.Parse("10000000-0000-4000-8000-000000000006");
        var dealerUser = Guid.Parse("10000000-0000-4000-8000-000000000008");
        var ceo = Guid.Parse("10000000-0000-4000-8000-000000000009");
        var org = new OrganizationSelection("C01", null, null);
        var now = DateTimeOffset.UtcNow.AddSeconds(1);
        var query = new ReportQuery();
        var result = reporting.Get(manager, org, query, now);
        check(result.Facts.All(x => x.CompanyId == "C01"), "P9: company boundary must precede aggregation.");
        check(!result.Kpis.Any(x => x.Definition.Key is "collection" or "overdue"), "P9: finance must not leak to sales manager or BI output.");
        var actualForecast = result.Kpis.Single(x => x.Definition.Key == "forecast");
        check(actualForecast.Value == result.Facts.Where(x => x.Metric == "forecast").Sum(x => x.Numerator), "P9: KPI and drilldown must reconcile.");
        var scoped = reporting.Get(expert, org, query with { Profile = "executive" }, now);
        check(scoped.Facts.All(x => x.BranchId == "B01") && scoped.Branches.All(x => x.Id == "B01"), "P9: role layout must never widen branch access.");
        check(!scoped.CanExport, "P9: expert export is disabled.");
        Reject<UnauthorizedAccessException>(() => reporting.Get(expert, org, query with { BranchId = "B03" }, now), "P9: forged branch filter", check);
        Reject<UnauthorizedAccessException>(() => reporting.Get(dealerUser, org, query, now), "P9: Dealer scope cannot become company analytics", check);
        Reject<UnauthorizedAccessException>(() => reporting.Export(expert, org, query with { Metric = "forecast" }, now, "test"), "P9: direct export permission", check);
        Reject<KeyNotFoundException>(() => reporting.Drilldown(manager, org, query with { Metric = "collection" }, now), "P9: financial metric enumeration", check);
        Reject<ArgumentException>(() => reporting.Get(manager, org, query with { From = new(2026, 9, 2), To = new(2026, 9, 1) }, now), "P9: reversed range", check);
        Reject<ArgumentException>(() => reporting.Get(manager, org, query with { From = new(2020, 1, 1), To = new(2026, 9, 1) }, now), "P9: bounded range", check);
        Reject<ArgumentException>(() => reporting.Drilldown(manager, org, query with { Metric = "forecast", PageSize = 101 }, now), "P9: page bounds", check);
        var zero = reporting.Get(manager, org, query with { From = new(2001, 1, 1), To = new(2001, 1, 2) }, now);
        check(zero.Kpis.Single(x => x.Definition.Key == "conversion").Value is null, "P9: denominator zero is not 0 percent.");
        check(zero.Kpis.Single(x => x.Definition.Key == "quality").RecordCount > 0, "P9: current customer quality is explicitly independent of period.");
        var financeReport = reporting.Get(finance, org, query, now);
        var collection = financeReport.Kpis.Single(x => x.Definition.Key == "collection");
        check(collection.Numerator == 1_250_000_000m && collection.Denominator == 1_900_000_000m, "P9: collection uses invoice facts, not order status or balances.");
        check(collection.StaleCount == 1, "P9: financial stale count is calculated.");
        check(reporting.Get(ceo, org, query, now).DefaultProfile == "executive", "P9: executive role receives the correct default profile.");
        var south = reporting.Get(manager, org, query with { RegionId = "R02" }, now);
        check(south.Facts.All(x => x.BranchId == "B03"), "P9: region resolves descendants without expanding access.");
        var page = reporting.Drilldown(manager, org, query with { Metric = "forecast", PageSize = 1 }, now);
        check(page.Rows.Count == 1 && page.TotalCount == actualForecast.RecordCount, "P9: pagination preserves full totals.");

        var managerAccess = access.Get(manager)!;
        var narrowExport = managerAccess with { PermissionScopeGrants = managerAccess.PermissionScopeGrants
            .Select(x => x.CompanyId == "C01" && x.Permission == "Reporting.Export" ? x with { ScopeType = "Branch", ScopeId = "B01" } : x).ToArray() };
        var narrowService = new ReportingService(store, new FixedAccess(narrowExport), new DemoReportingFinanceSource());
        var narrowCsv = Encoding.UTF8.GetString(narrowService.Export(manager, org, query with { Metric = "quality" }, now, "p9-export-scope").Content);
        check(narrowCsv.Contains("\"B01\"") && !narrowCsv.Contains("\"B02\"") && !narrowCsv.Contains("\"B03\""),
            "P9: export intersects its own branch grant with broader read grants.");
        var narrowSource = managerAccess with { PermissionScopeGrants = managerAccess.PermissionScopeGrants
            .Select(x => x.CompanyId == "C01" && x.Permission == "Customer.Read" ? x with { ScopeType = "Branch", ScopeId = "B01" } : x).ToArray() };
        var sourceReport = new ReportingService(store, new FixedAccess(narrowSource), new DemoReportingFinanceSource()).Get(manager, org, query, now);
        check(sourceReport.Facts.Where(x => x.Metric == "quality").All(x => x.BranchId == "B01"),
            "P9: source-module scope remains enforced under company reporting permission.");

        var customerId = Guid.Parse("20000000-0000-4000-8000-000000000001");
        var invoice = new ReceivableFact("VALID", customerId, "C01", "B01", "T01", now.AddDays(-1), now.AddDays(1), 100, 40, "IRR", now, "test");
        var source = new FixedFinance(new[] { invoice, invoice with { CollectedAmount = 10, SynchronizedAtUtc = now.AddMinutes(-1) },
            invoice with { InvoiceId = "FOREIGN", CompanyId = "C02" }, invoice with { InvoiceId = "MISMATCH", BranchId = "B03" },
            invoice with { InvoiceId = "FUTURE", SynchronizedAtUtc = now.AddDays(1) }, invoice with { InvoiceId = "BAD", CollectedAmount = 101 } });
        var sourceTest = new ReportingService(store, access, source).Get(finance, org, query with { From = DateOnly.FromDateTime(now.AddDays(-2).UtcDateTime) }, now);
        check(sourceTest.Kpis.Single(x => x.Definition.Key == "collection").Numerator == 40 && sourceTest.Facts.Count(x => x.Metric == "collection") == 1,
            "P9: latest valid invoice is counted once; foreign, inconsistent and future facts are excluded.");
        check(sourceTest.Kpis.Single(x => x.Definition.Key == "overdue").Value == 0,
            "P9: known invoices with no overdue balance display zero, not missing.");

        var oldSales = result.Kpis.Single(x => x.Definition.Key == "dealerSales").Value;
        store.Write(data =>
        {
            var seed = data.DealerPerformanceSnapshots.Single();
            data.DealerPerformanceSnapshots.Add(new DealerPerformanceSnapshot(Guid.NewGuid(), seed.CompanyId, seed.BranchId, seed.TerritoryId,
                seed.DealerId, seed.PeriodFromUtc, seed.PeriodToUtc, seed.NetSales, seed.OrderCount, seed.Source, now));
            data.DealerPerformanceSnapshots.Add(new DealerPerformanceSnapshot(Guid.NewGuid(), seed.CompanyId, seed.BranchId == "B01" ? "B03" : "B01", seed.TerritoryId,
                seed.DealerId, seed.PeriodFromUtc, seed.PeriodToUtc, 999_999_999_999m, seed.OrderCount, seed.Source, now));
            return true;
        });
        check(reporting.Get(manager, org, query, now).Kpis.Single(x => x.Definition.Key == "dealerSales").Value == oldSales,
            "P9: repeated sync and mismatched branch snapshots must not inflate dealer performance.");
        store.Write(data =>
        {
            foreach (var target in data.DealerTargets)
                target.Update(target.PeriodFromUtc.AddDays(1), target.PeriodToUtc, target.Amount, target.Source, manager);
            return true;
        });
        var unmatchedTarget = reporting.Get(manager, org, query, now).Kpis.Single(x => x.Definition.Key == "dealerAchievement");
        check(unmatchedTarget.Value is null && unmatchedTarget.MissingCount > 0,
            "P9: mismatched target periods must be excluded and marked missing.");
        var beforeAudit = store.Read(data => data.SecurityAuditEvents.Count);
        var csv = reporting.Export(manager, org, query with { Metric = "forecast" }, now, "p9-csv-test");
        var text = Encoding.UTF8.GetString(csv.Content);
        check(text.Contains("crm.reporting.v1") && !text.Contains("DEMO-INV"), "P9: CSV carries schema and excludes inaccessible financial data.");
        var bi = reporting.ExportBi(manager, org, query, now, "p9-bi-test");
        check(bi.Facts.All(x => x.Metric is not ("collection" or "overdue")), "P9: BI uses the same masking policy.");
        check(store.Read(data => data.SecurityAuditEvents.Count) == beforeAudit + 2, "P9: both export formats must audit.");
        check(ReportingService.CsvCell(" =HYPERLINK(\"x\")").StartsWith("\"'"), "P9: leading-space formula injection must be neutralized.");
        check(ReportingService.CsvCell("\t=1").StartsWith("\"'"), "P9: tab formula injection must be neutralized.");

        // Boundary and formula tests on an isolated, deterministic cohort.
        store.Write(data =>
        {
            data.Opportunities.Clear(); data.Leads.Clear();
            var start = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
            data.Opportunities.Add(new Opportunity(Guid.NewGuid(), "BOUND-1", "=danger", "نمونه", Guid.NewGuid(), 1000, "نمونه", "C01", "B01", "T01", expectedCloseAtUtc: start));
            data.Opportunities.Add(new Opportunity(Guid.NewGuid(), "BOUND-2", "excluded", "نمونه", Guid.NewGuid(), 999999, "نمونه", "C01", "B01", "T01", expectedCloseAtUtc: start.AddDays(1)));
            data.Leads.Add(new Lead(Guid.NewGuid(), "L-1", "سرنخ روز", "", "test", "", "C01", "B01", "T01") { CreatedAtUtc = start });
            return true;
        });
        var day = query with { From = new(2026, 9, 1), To = new(2026, 9, 1) };
        var bounded = reporting.Get(manager, org, day, now);
        check(bounded.Kpis.Single(x => x.Definition.Key == "forecast").Value == 100m, "P9: [start,end) boundaries and weighted forecast formula.");
        var exported = Encoding.UTF8.GetString(reporting.Export(manager, org, day with { Metric = "forecast" }, now, "p9-formula").Content);
        check(exported.Contains("\"'=danger\""), "P9: formula neutralization must apply to actual exported data.");
        store.Write(data =>
        {
            for (var i = 0; i < ReportingService.ExportLimit + 1; i++)
                data.Leads.Add(new Lead(Guid.NewGuid(), "CAP-" + i, "ردیف", "", "test", "", "C01", "B01", "T01") { CreatedAtUtc = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero) });
            return true;
        });
        Reject<InvalidOperationException>(() => reporting.Export(manager, org, day with { Metric = "conversion" }, now, "p9-limit"), "P9: exports reject truncation over 5000 rows", check);
    }

    private static void Reject<T>(Action action, string message, Action<bool, string> check) where T : Exception
    {
        try { action(); check(false, message + " was not rejected."); }
        catch (T) { check(true, message); }
    }

    private sealed class FixedAccess(AccessSnapshot snapshot) : IAccessSnapshotService
    {
        public AccessSnapshot? Get(Guid userId) => snapshot.UserId == userId ? snapshot : null;
        public bool HasPermission(Guid userId, string companyId, string permission) => Get(userId)?.PermissionsFor(companyId).Contains(permission) == true;
        public void Invalidate(Guid userId) { }
    }

    private sealed class FixedFinance(IReadOnlyList<ReceivableFact> facts) : IReportingFinanceSource
    {
        public IReadOnlyList<ReceivableFact> Read(string companyId, IReadOnlySet<Guid> allowedCustomerIds, DateTimeOffset nowUtc) => facts;
    }
}
