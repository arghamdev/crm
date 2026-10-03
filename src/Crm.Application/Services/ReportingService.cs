using System.Globalization;
using System.Text;
using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Domain.Channel;
using Crm.Domain.Customers;
using Crm.Domain.Identity;
using Crm.Domain.Organization;
using Crm.Domain.Sales;

namespace Crm.Application.Services;

public sealed class ReportingService(ICrmDataStore store, IAccessSnapshotService access,
    IReportingFinanceSource financeSource) : IReportingService
{
    public const string SchemaVersion = "crm.reporting.v1";
    public const int ExportLimit = 5000;
    public static readonly IReadOnlyList<KpiDefinition> Catalog = Array.AsReadOnly(new[]
    {
        new KpiDefinition("pipeline", "ارزش فرصت‌های باز", "ریال", "Σ ارزش فرصت باز", "CRM Opportunity", "تاریخ بستن مورد انتظار در بازه", "مدیر فروش", "درآمد قطعی نیست؛ واحد نمونه IRR"),
        new KpiDefinition("forecast", "پیش‌بینی وزنی", "ریال", "Σ (ارزش × احتمال مرحله ÷ ۱۰۰)", "CRM Opportunity", "تاریخ بستن مورد انتظار در بازه", "مدیر فروش", "سناریوی وزنی، نه تعهد یا پیش‌بینی هوش مصنوعی"),
        new KpiDefinition("won", "ارزش فرصت‌های برنده", "ریال", "Σ ارزش فرصت Won", "CRM Opportunity", "ClosedAtUtc در بازه", "مدیر فروش", "ارزش قرارداد بالقوه؛ معادل فروش فاکتورشده نیست"),
        new KpiDefinition("conversion", "تبدیل سرنخ به فرصت", "درصد", "۱۰۰ × سرنخ تبدیل‌شده ÷ همه سرنخ‌های همان cohort", "CRM Lead", "سرنخ‌های ایجادشده در بازه؛ وضعیت فعلی", "مدیر فروش", "مخرج صفر = ناموجود؛ نرخ تاریخی بازسازی‌شده نیست"),
        new KpiDefinition("quality", "کامل‌بودن پرونده مشتری", "درصد", "۱۰۰ × فیلدهای تکمیل‌شده ÷ (۵ × تعداد مشتری)", "CRM Customer", "تصویر فعلی مشتریان غیرغیرفعال؛ مستقل از بازه", "مدیر داده", "نام، شهر، شناسه ملی، تلفن، ایمیل؛ اعتبارسنجی صحت نیست"),
        new KpiDefinition("collection", "نسبت وصول فاکتورهای دوره", "درصد", "۱۰۰ × وصول تجمعی ÷ مبلغ فاکتورهای همان cohort", "Accounting Demo", "تاریخ فاکتور در بازه؛ وصول تا زمان Snapshot", "مدیر مالی", "وصول در تاریخ پرداخت نیست؛ برگشت و اعتبارنامه نیازمند Adapter واقعی"),
        new KpiDefinition("overdue", "مانده سررسیدشده دوره", "ریال", "Σ (فاکتور − وصول) برای سررسید قبل از اکنون", "Accounting Demo", "تاریخ فاکتور در بازه؛ وضعیت فعلی", "مدیر مالی", "با مانده نماینده جمع نمی‌شود؛ فقط IRR"),
        new KpiDefinition("dealerAchievement", "تحقق هدف نمایندگان", "درصد", "۱۰۰ × Σ فروش خالص ÷ Σ هدفِ دوره دقیقاً یکسان", "ERP/BI + CRM Target", "آخرین دوره مشترک داخل بازه؛ آخرین Snapshot هر نماینده", "مدیر کانال", "دوره‌های متفاوت یا هدف مفقود از نسبت حذف و علامت‌گذاری می‌شوند"),
        new KpiDefinition("dealerSales", "فروش خالص نمایندگان", "ریال", "Σ آخرین Snapshot فروش برای هر نماینده", "ERP/BI Dealer", "آخرین دوره مشترک داخل بازه؛ آخرین Snapshot هر نماینده", "مدیر کانال", "با فروش مستقیم جمع نمی‌شود؛ نماینده فاقد همان دوره علامت‌گذاری می‌شود")
    });

    public ReportingDashboard Get(Guid userId, OrganizationSelection organization, ReportQuery query, DateTimeOffset nowUtc)
    {
        var snapshot = Require(userId, organization, "Reporting.Read");
        return store.Read(data => Build(data, snapshot, organization, query, nowUtc));
    }

    public ReportDrilldown Drilldown(Guid userId, OrganizationSelection organization, ReportQuery query, DateTimeOffset nowUtc)
    {
        if (query.Page < 1 || query.Page > 100000 || query.PageSize < 1 || query.PageSize > 100)
            throw new ArgumentException("صفحه یا اندازه صفحه معتبر نیست؛ حداکثر ۱۰۰ ردیف.");
        var result = Get(userId, organization, query, nowUtc);
        var kpi = result.Kpis.SingleOrDefault(x => x.Definition.Key == query.Metric)
            ?? throw new KeyNotFoundException("شاخص مجاز پیدا نشد.");
        var rows = result.Facts.Where(x => x.Metric == query.Metric).OrderBy(x => x.Code, StringComparer.Ordinal).ThenBy(x => x.RecordId).ToArray();
        return new(result, kpi, rows.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToArray(), query.Page, query.PageSize, rows.Length);
    }

    public ReportExport Export(Guid userId, OrganizationSelection organization, ReportQuery query, DateTimeOffset nowUtc, string correlationId)
    {
        var snapshot = Require(userId, organization, "Reporting.Export");
        return store.Write(data =>
        {
            // Recompute in the same store operation used to persist the audit receipt.
            var report = Build(data, snapshot, organization, query, nowUtc, "Reporting.Export");
            var metric = report.Kpis.SingleOrDefault(x => x.Definition.Key == query.Metric)
                ?? throw new KeyNotFoundException("شاخص مجاز پیدا نشد.");
            var rows = report.Facts.Where(x => x.Metric == metric.Definition.Key).OrderBy(x => x.Code).ThenBy(x => x.RecordId).ToArray();
            if (rows.Length > ExportLimit) throw new InvalidOperationException("خروجی بیش از ۵۰۰۰ ردیف است؛ بازه یا دامنه را محدود کنید.");
            var csv = new StringBuilder("schema,generated_utc,from_utc,to_inclusive_utc,metric,record_id,code,name,company,branch,territory,state,numerator,denominator,unit,source_utc,source\r\n");
            foreach (var row in rows)
            {
                var cells = new[] { SchemaVersion, nowUtc.ToString("O"), report.Filter.From.ToString("yyyy-MM-dd"), report.Filter.To.ToString("yyyy-MM-dd"), row.Metric,
                    row.RecordId, row.Code, row.Name, row.CompanyId, row.BranchId, row.TerritoryId ?? "", row.State,
                    row.Numerator.ToString(CultureInfo.InvariantCulture), row.Denominator.ToString(CultureInfo.InvariantCulture), metric.Definition.Unit,
                    row.SourceAtUtc?.ToString("O") ?? "", row.Source };
                csv.Append(string.Join(',', cells.Select(CsvCell))).Append("\r\n");
            }
            Audit(data, userId, organization, report, nowUtc, correlationId, "Csv", query.Metric!, rows.Length);
            return new ReportExport($"crm-{query.Metric}-{nowUtc:yyyyMMdd-HHmmss}.csv", "text/csv; charset=utf-8",
                Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray());
        });
    }

    public ReportingDashboard ExportBi(Guid userId, OrganizationSelection organization, ReportQuery query, DateTimeOffset nowUtc, string correlationId)
    {
        var snapshot = Require(userId, organization, "Reporting.BiExport");
        return store.Write(data =>
        {
            var report = Build(data, snapshot, organization, query, nowUtc, "Reporting.BiExport");
            if (report.Facts.Count > ExportLimit) throw new InvalidOperationException("مجموع Factها بیش از ۵۰۰۰ است؛ دامنه خروجی را محدود کنید.");
            Audit(data, userId, organization, report, nowUtc, correlationId, "BiJson", "all", report.Facts.Count);
            return report;
        });
    }

    public static string CsvCell(string value)
    {
        // Quote every cell and neutralize formulas even after whitespace/control characters.
        var candidate = value.TrimStart();
        if (candidate.Length > 0 && "=+-@".Contains(candidate[0]) || value.Any(c => c is '\t' or '\r' or '\n')) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private AccessSnapshot Require(Guid userId, OrganizationSelection organization, string permission)
    {
        var snapshot = access.Get(userId) ?? throw new UnauthorizedAccessException("نشست معتبر نیست.");
        if (!snapshot.PermissionsFor(organization.CompanyId).Contains("Reporting.Read") ||
            !snapshot.PermissionsFor(organization.CompanyId).Contains(permission))
            throw new UnauthorizedAccessException("مجوز گزارش وجود ندارد.");
        return snapshot;
    }

    private ReportingDashboard Build(CrmDataSet data, AccessSnapshot snapshot, OrganizationSelection org,
        ReportQuery query, DateTimeOffset now, string reportPermission = "Reporting.Read")
    {
        var defaultProfile = snapshot.ScopeGrants.Any(x => x.CompanyId == org.CompanyId && x.RoleKey == "Executive") ? "executive" :
            snapshot.ScopeGrants.Any(x => x.CompanyId == org.CompanyId && x.RoleKey == "FinanceManager") ? "finance" :
            snapshot.ScopeGrants.Any(x => x.CompanyId == org.CompanyId && x.RoleKey == "ChannelManager") ? "channel" :
            org.BranchId is not null ? "branch" : "sales";
        var profile = query.Profile ?? defaultProfile;
        if (!new[] { "executive", "sales", "region", "branch", "finance", "channel" }.Contains(profile))
            throw new ArgumentException("نمای گزارش معتبر نیست.");
        var from = query.From ?? new DateOnly(now.Year, now.Month, 1);
        var to = query.To ?? new DateOnly(now.Year, now.Month, 1).AddMonths(2).AddDays(-1);
        if (to < from || to.DayNumber - from.DayNumber > 365 || to == DateOnly.MaxValue || from.Year < 2000)
            throw new ArgumentException("بازه باید از سال ۲۰۰۰ به بعد و بین ۱ تا ۳۶۶ روز باشد.");
        var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var end = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        bool InPeriod(DateTimeOffset date) => date >= start && date < end;
        string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        var branchId = Clean(query.BranchId); var regionId = Clean(query.RegionId); var territoryId = Clean(query.TerritoryId);
        if (branchId?.Length > 64 || regionId?.Length > 64 || territoryId?.Length > 64) throw new ArgumentException("فیلتر معتبر نیست.");
        // A layout/profile never grants permissions. Every fact needs both report and underlying data permission.
        bool BaseAccess(IOrganizationScoped row, string permission) => row.CompanyId == org.CompanyId &&
            (org.BranchId is null || row.BranchId == org.BranchId) && (org.TerritoryId is null || row.TerritoryId == org.TerritoryId) &&
            snapshot.AllowsRecord(row.CompanyId, "Reporting.Read", row.BranchId, row.TerritoryId) &&
            snapshot.AllowsRecord(row.CompanyId, reportPermission, row.BranchId, row.TerritoryId) &&
            snapshot.AllowsRecord(row.CompanyId, permission, row.BranchId, row.TerritoryId);
        var availableRows = data.Opportunities.Where(x => BaseAccess(x, "Opportunity.Read")).Cast<IOrganizationScoped>()
            .Concat(data.Leads.Where(x => BaseAccess(x, "Lead.Read")))
            .Concat(data.Customers.Where(x => BaseAccess(x, "Customer.Read")))
            .Concat(data.Dealers.Where(x => BaseAccess(x, "Dealer.Read"))).ToArray();
        // Options expose only already-visible data dimensions, not the whole organization.
        var branchIds = availableRows.Select(x => x.BranchId).ToHashSet();
        var branches = data.OrganizationUnits.Where(x => x.CompanyId == org.CompanyId && x.Type == OrganizationUnitType.Branch && branchIds.Contains(x.UnitId))
            .OrderBy(x => x.UnitId).Select(x => new ReportOption(x.UnitId, x.Name)).ToArray();
        bool InRegion(string id, string region)
        {
            var seen = new HashSet<string>();
            string? current = id;
            while (current is not null && seen.Add(current))
            {
                if (current == region) return true;
                current = data.OrganizationUnits.SingleOrDefault(x => x.CompanyId == org.CompanyId && x.UnitId == current)?.ParentUnitId;
            }
            return false;
        }
        var regions = data.OrganizationUnits.Where(x => x.CompanyId == org.CompanyId && x.Type == OrganizationUnitType.Region && branchIds.Any(b => InRegion(b, x.UnitId)))
            .Select(x => new ReportOption(x.UnitId, x.Name)).ToArray();
        var visibleTerritories = availableRows.Select(x => x.TerritoryId).ToHashSet();
        var territories = data.Territories.Where(x => x.CompanyId == org.CompanyId && visibleTerritories.Contains(x.TerritoryId))
            .Select(x => new ReportOption(x.TerritoryId, x.Name)).ToArray();
        if (branchId is not null && !branches.Any(x => x.Id == branchId) || regionId is not null && !regions.Any(x => x.Id == regionId) ||
            territoryId is not null && !territories.Any(x => x.Id == territoryId)) throw new UnauthorizedAccessException("فیلتر خارج از دامنه مجاز است.");
        if (branchId is not null && regionId is not null && !InRegion(branchId, regionId)) throw new ArgumentException("شعبه در منطقه انتخاب‌شده نیست.");
        bool Allowed(IOrganizationScoped row, string permission) => BaseAccess(row, permission) &&
            (branchId is null || row.BranchId == branchId) && (territoryId is null || row.TerritoryId == territoryId) &&
            (regionId is null || InRegion(row.BranchId, regionId));
        var facts = new List<ReportFact>();
        var funnel = new List<ReportFunnelStage>();
        var warnings = new List<string> { "نسخه نمونه؛ تاریخ ورودی میلادی و مرز روزها UTC است. اطلاعات، تصویر فعلی‌اند و تاریخچهٔ As-of نیستند.",
            "انتخاب نمای مدیرعامل/منطقه صرفاً چیدمان است؛ Company و Scope نشست و مجوز منبع همچنان اعمال می‌شوند.",
            "Forecast به احتمال مرحله متکی است. فروش نمایندگان، فرصت برنده و وصول را با یکدیگر جمع نکنید." };
        var enabled = new HashSet<string>();
        bool Permission(string p) => snapshot.PermissionsFor(org.CompanyId).Contains(p);
        void Add(string metric, string id, string code, string name, IOrganizationScoped row, string state,
            decimal numerator, decimal denominator, DateTimeOffset? at, string source, string url) =>
            facts.Add(new(metric, id, code, name, row.CompanyId, row.BranchId, row.TerritoryId, state,
                numerator, denominator, metric is "conversion" or "quality" ? "N/A" : "IRR", at, source, url));
        if (Permission("Opportunity.Read"))
        {
            enabled.UnionWith(new[] { "pipeline", "forecast", "won" });
            foreach (var opportunity in data.Opportunities.Where(x => Allowed(x, "Opportunity.Read")))
            {
                if (opportunity.Stage is not (OpportunityStage.Won or OpportunityStage.Lost) && InPeriod(opportunity.ExpectedCloseAtUtc))
                {
                    Add("pipeline", opportunity.Id.ToString(), opportunity.Code, opportunity.Title, opportunity, opportunity.Stage.ToString(), opportunity.Value, 0, now, "CRM live", $"/opportunities/{opportunity.Id}");
                    Add("forecast", opportunity.Id.ToString(), opportunity.Code, opportunity.Title, opportunity, opportunity.Stage.ToString(), opportunity.Value * opportunity.Probability / 100m, 0, now, "CRM live", $"/opportunities/{opportunity.Id}");
                }
                if (opportunity.Stage == OpportunityStage.Won && opportunity.ClosedAtUtc is { } closed && InPeriod(closed))
                    Add("won", opportunity.Id.ToString(), opportunity.Code, opportunity.Title, opportunity, "Won", opportunity.Value, 0, now, "CRM live", $"/opportunities/{opportunity.Id}");
            }
        }
        if (Permission("Lead.Read"))
        {
            enabled.Add("conversion");
            var cohort = data.Leads.Where(x => Allowed(x, "Lead.Read") && InPeriod(x.CreatedAtUtc)).ToArray();
            var counts = new[] { ("سرنخ‌های دوره", cohort.Length), ("تماس ثبت‌شده", cohort.Count(x => x.FirstContactAtUtc.HasValue)),
                ("واجد شرایط یا تبدیل‌شده", cohort.Count(x => x.Status is LeadStatus.Qualified or LeadStatus.Converted)),
                ("تبدیل‌شده", cohort.Count(x => x.Status == LeadStatus.Converted)) };
            funnel.AddRange(counts.Select(x => new ReportFunnelStage(x.Item1, x.Item2, cohort.Length == 0 ? null : 100m * x.Item2 / cohort.Length)));
            foreach (var lead in cohort)
                Add("conversion", lead.Id.ToString(), lead.Code, lead.Name, lead, lead.Status.ToString(), lead.Status == LeadStatus.Converted ? 1 : 0, 1, now, "CRM cohort", $"/leads/{lead.Id}");
        }
        var customers = data.Customers.Where(x => x.Status != CustomerStatus.Inactive && Allowed(x, "Customer.Read")).ToArray();
        if (Permission("Customer.Read"))
        {
            enabled.Add("quality");
            foreach (var customer in customers)
            {
                var fields = new[] { customer.Name, customer.City, customer.NationalId, customer.PrimaryPhone, customer.PrimaryEmail };
                Add("quality", customer.Id.ToString(), customer.Code, customer.Name, customer, customer.Status.ToString(), fields.Count(x => !string.IsNullOrWhiteSpace(x)), 5, now, "CRM master", $"/customers/{customer.Id}");
            }
        }
        if (Permission("Reporting.Financial.Read") && Permission("Customer.Read"))
        {
            enabled.UnionWith(new[] { "collection", "overdue" });
            var financialCustomers = customers.Where(x => Allowed(x, "Reporting.Financial.Read")).ToDictionary(x => x.Id);
            var invoices = financeSource.Read(org.CompanyId, financialCustomers.Keys.ToHashSet(), now)
                .Where(x => x.CompanyId == org.CompanyId && financialCustomers.ContainsKey(x.CustomerId) && x.SynchronizedAtUtc <= now)
                .GroupBy(x => x.InvoiceId).Select(x => x.OrderByDescending(i => i.SynchronizedAtUtc).First()).ToArray();
            foreach (var invoice in invoices.Where(x => InPeriod(x.InvoiceAtUtc)))
            {
                var customer = financialCustomers[invoice.CustomerId];
                if (invoice.BranchId != customer.BranchId || invoice.TerritoryId != customer.TerritoryId || invoice.Currency != "IRR" ||
                    invoice.InvoicedAmount <= 0 || invoice.CollectedAmount < 0 || invoice.CollectedAmount > invoice.InvoicedAmount ||
                    string.IsNullOrWhiteSpace(invoice.InvoiceId) || string.IsNullOrWhiteSpace(invoice.Source) || invoice.InvoiceAtUtc > now ||
                    invoice.SynchronizedAtUtc < invoice.InvoiceAtUtc || invoice.DueAtUtc < invoice.InvoiceAtUtc)
                { warnings.Add("یک Fact مالی ناسازگار با واحد پول، دامنه یا مبلغ حذف شد؛ بررسی تطبیق لازم است."); continue; }
                Add("collection", invoice.InvoiceId, invoice.InvoiceId, customer.Name, customer, "Invoice cohort", invoice.CollectedAmount, invoice.InvoicedAmount, invoice.SynchronizedAtUtc, invoice.Source, $"/customers/{customer.Id}");
                var overdue = invoice.DueAtUtc < now ? invoice.InvoicedAmount - invoice.CollectedAmount : 0;
                Add("overdue", invoice.InvoiceId, invoice.InvoiceId, customer.Name, customer, overdue > 0 ? "Overdue" : "No overdue", overdue, 0, invoice.SynchronizedAtUtc, invoice.Source, $"/customers/{customer.Id}");
            }
        }
        else warnings.Add("اطلاعات وصول و سررسید بدون مجوز مالی گزارش، نمایش یا صادر نمی‌شود.");
        var missingDealerPeriods = 0;
        if (Permission("Dealer.Read"))
        {
            enabled.UnionWith(new[] { "dealerAchievement", "dealerSales" });
            var dealers = data.Dealers.Where(x => Allowed(x, "Dealer.Read")).ToArray();
            var dealerById = dealers.ToDictionary(x => x.Id);
            var comparable = data.DealerPerformanceSnapshots.Where(x => dealerById.TryGetValue(x.DealerId, out var owner) && x.CompanyId == org.CompanyId &&
                x.BranchId == owner.BranchId && x.TerritoryId == owner.TerritoryId &&
                x.PeriodFromUtc >= start && x.PeriodToUtc <= end && x.SynchronizedAtUtc <= now).ToArray();
            var commonPeriod = comparable.OrderByDescending(x => x.PeriodToUtc).ThenByDescending(x => x.PeriodFromUtc).FirstOrDefault();
            foreach (var dealer in dealers)
            {
                var actual = comparable.Where(x => x.DealerId == dealer.Id && x.PeriodFromUtc == commonPeriod?.PeriodFromUtc && x.PeriodToUtc == commonPeriod?.PeriodToUtc)
                    .OrderByDescending(x => x.SynchronizedAtUtc).ThenBy(x => x.Id).FirstOrDefault();
                if (actual is null) { missingDealerPeriods++; continue; }
                var target = data.DealerTargets.SingleOrDefault(x => x.DealerId == dealer.Id && x.CompanyId == org.CompanyId &&
                    x.BranchId == dealer.BranchId && x.TerritoryId == dealer.TerritoryId && x.PeriodFromUtc == actual.PeriodFromUtc && x.PeriodToUtc == actual.PeriodToUtc);
                var periodLabel = $"{Crm.Domain.Common.JalaliDate.Format(actual.PeriodFromUtc.UtcDateTime)} تا {Crm.Domain.Common.JalaliDate.Format(actual.PeriodToUtc.UtcDateTime)} (پایان غیرشامل)";
                Add("dealerSales", dealer.Id.ToString(), dealer.Code, dealer.TradeName, dealer, periodLabel, actual.NetSales, 0, actual.SynchronizedAtUtc, actual.Source, $"/dealers/{dealer.Id}");
                if (target is null) { missingDealerPeriods++; continue; }
                Add("dealerAchievement", dealer.Id.ToString(), dealer.Code, dealer.TradeName, dealer, periodLabel, actual.NetSales, target.Amount, actual.SynchronizedAtUtc, actual.Source, $"/dealers/{dealer.Id}");
            }
        }
        if (missingDealerPeriods > 0) warnings.Add($"{missingDealerPeriods} نماینده فاقد عملکرد یا هدف هم‌دوره است؛ در نسبت تحقق هدف لحاظ نشده است.");
        var definitions = Catalog.Where(x => enabled.Contains(x.Key)).ToArray();
        var kpis = definitions.Select(definition =>
        {
            var rows = facts.Where(x => x.Metric == definition.Key).ToArray();
            return new ReportKpi(definition, Value(definition, rows), rows.Sum(x => x.Numerator), rows.Sum(x => x.Denominator), rows.Length,
                rows.Count(x => x.SourceAtUtc is null || now - x.SourceAtUtc > TimeSpan.FromMinutes(15)), definition.Key == "dealerAchievement" ? missingDealerPeriods : 0);
        }).ToArray();
        var breakdown = facts.GroupBy(x => new { x.BranchId, x.Metric }).Select(group => new ReportBreakdown(group.Key.BranchId,
            branches.SingleOrDefault(x => x.Id == group.Key.BranchId)?.Name ?? group.Key.BranchId, group.Key.Metric,
            Value(definitions.Single(x => x.Key == group.Key.Metric), group.ToArray()), group.Count())).ToArray();
        return new(SchemaVersion, org.CompanyId, now, new(from, to, regionId, branchId, territoryId, profile), defaultProfile,
            regions, branches, territories, kpis, breakdown, funnel, facts.ToArray(), warnings.Distinct().ToArray(),
            Permission("Reporting.Export"), Permission("Reporting.BiExport"));
    }

    private static decimal? Value(KpiDefinition definition, IReadOnlyList<ReportFact> rows) => definition.Unit == "درصد"
        ? rows.Sum(x => x.Denominator) > 0 ? 100m * rows.Sum(x => x.Numerator) / rows.Sum(x => x.Denominator) : null
        : rows.Count == 0 ? null : rows.Sum(x => x.Numerator);

    private static void Audit(CrmDataSet data, Guid actor, OrganizationSelection org, ReportingDashboard report,
        DateTimeOffset now, string correlation, string format, string metric, int count)
    {
        data.Append<Crm.Domain.Identity.SecurityAuditEvent>(new SecurityAuditEvent(Guid.NewGuid(), now, "Reporting.Export", "Succeeded", actor, null, null,
            correlation, $"Company={org.CompanyId};SessionBranch={org.BranchId};SessionTerritory={org.TerritoryId};From={report.Filter.From:yyyy-MM-dd};To={report.Filter.To:yyyy-MM-dd};Region={report.Filter.RegionId};Branch={report.Filter.BranchId};Territory={report.Filter.TerritoryId};Format={format};Metric={metric};Rows={count};Schema={SchemaVersion}", "", ""));
    }
}
