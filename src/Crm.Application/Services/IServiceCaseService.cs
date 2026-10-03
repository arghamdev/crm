using Crm.Application.Contracts;
using Crm.Domain.Service;

namespace Crm.Application.Services;

public interface IServiceCaseService
{
    ServiceCaseListDto GetCases(Guid currentUserId, OrganizationSelection organization, string? query = null,
        ServiceCaseStatus? status = null, ServiceCasePriority? priority = null, bool includeClosed = false,
        bool onlyMine = false, DateTimeOffset? nowUtc = null);
    ServiceCaseDetailsDto? GetCase(Guid currentUserId, OrganizationSelection organization, Guid id, DateTimeOffset? nowUtc = null);
    IReadOnlyList<ServiceCaseCustomerOptionDto> GetCustomerOptions(Guid currentUserId, OrganizationSelection organization);
    ServiceCaseDto CreateCase(Guid currentUserId, OrganizationSelection organization, CreateServiceCaseCommand command, DateTimeOffset nowUtc);
    ServiceCaseDto TriageCase(Guid currentUserId, OrganizationSelection organization, Guid id, TriageServiceCaseCommand command, DateTimeOffset nowUtc);
    ServiceCaseDto ApplyAction(Guid currentUserId, OrganizationSelection organization, Guid id, ServiceCaseActionCommand command, DateTimeOffset nowUtc);
    ServiceEscalationResult RunEscalation(Guid currentUserId, OrganizationSelection organization, DateTimeOffset nowUtc);

    /// <summary>System job: escalates SLA breaches across all companies. Not user-scoped; called by the background worker.</summary>
    ServiceEscalationResult EscalateBreaches(DateTimeOffset nowUtc);
}
