using Crm.Application.Contracts;

namespace Crm.Application.Services;

public interface ICrmApplicationService
{
    DashboardDto GetDashboard(Guid currentUserId, OrganizationSelection organization);
    IReadOnlyList<CustomerDto> GetCustomers(Guid currentUserId, OrganizationSelection organization, string? query = null);
    PagedResult<CustomerDto> SearchCustomers(Guid currentUserId, OrganizationSelection organization, string? query = null, int page = 1, int pageSize = 20);
    CustomerDto? GetCustomer(Guid currentUserId, OrganizationSelection organization, Guid id);
    CustomerDto CreateCustomer(Guid currentUserId, OrganizationSelection organization, CreateCustomerCommand command, CustomerLogoUpload? logo = null);
    IReadOnlyList<LeadDto> GetLeads(Guid currentUserId, OrganizationSelection organization);
    LeadDto CreateLead(Guid currentUserId, OrganizationSelection organization, CreateLeadCommand command);
    OpportunityDto ConvertLead(Guid currentUserId, OrganizationSelection organization, Guid id);
    IReadOnlyList<OpportunityDto> GetOpportunities(Guid currentUserId, OrganizationSelection organization);
    OpportunityDto AdvanceOpportunity(Guid currentUserId, OrganizationSelection organization, Guid id);
    IReadOnlyList<QuoteDto> GetQuotes(Guid currentUserId, OrganizationSelection organization);
    QuoteDto CreateQuote(Guid currentUserId, OrganizationSelection organization, CreateQuoteCommand command);
    QuoteDto DecideQuote(Guid currentUserId, OrganizationSelection organization, Guid id, bool approved);
    IReadOnlyList<WorkItemDto> GetWorkItems(Guid currentUserId, OrganizationSelection organization);
    WorkItemDto CompleteWorkItem(Guid currentUserId, OrganizationSelection organization, Guid id);
}
