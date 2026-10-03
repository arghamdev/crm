using Crm.Application.Contracts;

namespace Crm.Application.Services;

public interface IReportingService
{
    ReportingDashboard Get(Guid userId, OrganizationSelection organization, ReportQuery query, DateTimeOffset nowUtc);
    ReportDrilldown Drilldown(Guid userId, OrganizationSelection organization, ReportQuery query, DateTimeOffset nowUtc);
    ReportExport Export(Guid userId, OrganizationSelection organization, ReportQuery query, DateTimeOffset nowUtc, string correlationId);
    ReportingDashboard ExportBi(Guid userId, OrganizationSelection organization, ReportQuery query, DateTimeOffset nowUtc, string correlationId);
}
