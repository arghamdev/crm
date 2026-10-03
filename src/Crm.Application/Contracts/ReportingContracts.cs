namespace Crm.Application.Contracts;

public sealed record ReportQuery(DateOnly? From = null, DateOnly? To = null,
    string? RegionId = null, string? BranchId = null, string? TerritoryId = null,
    string? Profile = null, string? Metric = null, int Page = 1, int PageSize = 25);

public sealed record ReportFilter(DateOnly From, DateOnly To, string? RegionId,
    string? BranchId, string? TerritoryId, string Profile);
public sealed record ReportOption(string Id, string Name);
public sealed record KpiDefinition(string Key, string Name, string Unit, string Formula,
    string Source, string PeriodRule, string Owner, string Limitation);
public sealed record ReportKpi(KpiDefinition Definition, decimal? Value, decimal Numerator,
    decimal Denominator, int RecordCount, int StaleCount, int MissingCount);
public sealed record ReportFact(string Metric, string RecordId, string Code, string Name,
    string CompanyId, string BranchId, string? TerritoryId, string State,
    decimal Numerator, decimal Denominator, string Currency,
    DateTimeOffset? SourceAtUtc, string Source, string Url);
public sealed record ReportBreakdown(string BranchId, string BranchName, string Metric,
    decimal? Value, int Count);
public sealed record ReportFunnelStage(string Name, int Count, decimal? Percent);
public sealed record ReportingDashboard(string SchemaVersion, string CompanyId,
    DateTimeOffset GeneratedAtUtc, ReportFilter Filter, string DefaultProfile,
    IReadOnlyList<ReportOption> Regions, IReadOnlyList<ReportOption> Branches,
    IReadOnlyList<ReportOption> Territories, IReadOnlyList<ReportKpi> Kpis,
    IReadOnlyList<ReportBreakdown> Breakdown, IReadOnlyList<ReportFunnelStage> Funnel, IReadOnlyList<ReportFact> Facts,
    IReadOnlyList<string> Warnings, bool CanExport, bool CanBiExport);
public sealed record ReportDrilldown(ReportingDashboard Dashboard, ReportKpi Kpi,
    IReadOnlyList<ReportFact> Rows, int Page, int PageSize, int TotalCount);
public sealed record ReportExport(string FileName, string ContentType, byte[] Content);
