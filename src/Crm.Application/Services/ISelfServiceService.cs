using Crm.Application.Contracts;

namespace Crm.Application.Services;

public interface ISelfServiceService
{
    PortalDashboardDto Portal(Guid userId, OrganizationSelection org, DateTimeOffset now);
    PortalRequestDto Submit(Guid userId, OrganizationSelection org, SubmitPortalRequestCommand command, DateTimeOffset now);
    PortalRequestDto Request(Guid userId, OrganizationSelection org, Guid id, DateTimeOffset now);
    void Cancel(Guid userId, OrganizationSelection org, Guid id, long expectedVersion, DateTimeOffset now);
    IReadOnlyList<PortalRequestDto> Inbox(Guid userId, OrganizationSelection org, DateTimeOffset now);
    PortalReviewDto ReviewForm(Guid userId, OrganizationSelection org, Guid id, DateTimeOffset now);
    PortalRequestDto Review(Guid userId, OrganizationSelection org, Guid id, ReviewPortalRequestCommand command, DateTimeOffset now);
    ReportExport Invoice(Guid userId, OrganizationSelection org, string invoiceId, DateTimeOffset now);
    MobileWorkspaceDto Mobile(Guid userId, OrganizationSelection org, DateOnly? day, DateTimeOffset now);
    MobileAck PlanVisit(Guid userId, OrganizationSelection org, CreateVisitCommand command, DateTimeOffset now);
    MobileAck VisitAction(Guid userId, OrganizationSelection org, Guid id, VisitActionCommand command, DateTimeOffset now);
}
