using Crm.Application.Contracts;
using Crm.Domain.Organization;

namespace Crm.Application.Services;

public interface IOrderApplicationService
{
    OrderWorkspaceDto GetWorkspace(Guid currentUserId, OrganizationSelection organization, DateTimeOffset nowUtc);
    OrderDetailsDto? Get(Guid currentUserId, OrganizationSelection organization, Guid id, DateTimeOffset nowUtc);
    IReadOnlyList<EligibleOrderQuoteDto> GetEligibleQuotes(Guid currentUserId, OrganizationSelection organization);
    OrderDetailsDto Create(Guid currentUserId, OrganizationSelection organization, CreateOrderRequestCommand command, DateTimeOffset nowUtc);
    OrderDetailsDto CheckCredit(Guid currentUserId, OrganizationSelection organization, Guid id, CheckOrderCreditCommand command, DateTimeOffset nowUtc);
    OrderDetailsDto OverrideCredit(Guid currentUserId, OrganizationSelection organization, Guid id, OverrideOrderCreditCommand command, DateTimeOffset nowUtc);
    OrderDetailsDto QueueSubmission(Guid currentUserId, OrganizationSelection organization, Guid id, QueueOrderSubmissionCommand command, DateTimeOffset nowUtc);
    OrderDetailsDto ProcessIntegration(Guid currentUserId, OrganizationSelection organization, Guid id, ProcessOrderIntegrationCommand command, DateTimeOffset nowUtc);
    OrderDetailsDto AdvanceProjection(Guid currentUserId, OrganizationSelection organization, Guid id, AdvanceOrderProjectionCommand command, DateTimeOffset nowUtc);
}
