using Crm.Application.Abstractions;
using Crm.Application.Contracts;

namespace Crm.Application.Services;

public interface ISalesPipelineService
{
    LeadListDto GetLeads(Guid currentUserId, OrganizationSelection organization, string? query = null,
        Crm.Domain.Sales.LeadStatus? status = null, bool includeClosed = false, DateTimeOffset? nowUtc = null);
    Task<LeadListDto> GetLeadsAsync(Guid currentUserId, OrganizationSelection organization, string? query = null,
        Crm.Domain.Sales.LeadStatus? status = null, bool includeClosed = false, int page = 1, int pageSize = PageRequest.DefaultPageSize,
        DateTimeOffset? nowUtc = null, CancellationToken cancellationToken = default);
    LeadDetailsDto? GetLead(Guid currentUserId, OrganizationSelection organization, Guid id, DateTimeOffset? nowUtc = null);
    IReadOnlyList<SalesOwnerOptionDto> GetEligibleOwners(Guid currentUserId, OrganizationSelection organization, string branchId);
    LeadDto CreateLead(Guid currentUserId, OrganizationSelection organization, CreateLeadCommand command, DateTimeOffset nowUtc);
    LeadDto AssignLead(Guid currentUserId, OrganizationSelection organization, Guid id, AssignLeadCommand command, DateTimeOffset nowUtc);
    LeadDto TransitionLead(Guid currentUserId, OrganizationSelection organization, Guid id, TransitionLeadCommand command, DateTimeOffset nowUtc);
    OpportunityDto ConvertLead(Guid currentUserId, OrganizationSelection organization, Guid id, ConvertLeadCommand command, DateTimeOffset nowUtc);

    PipelineBoardDto GetPipeline(Guid currentUserId, OrganizationSelection organization, string? query = null,
        bool includeClosed = false, DateTimeOffset? nowUtc = null);
    Task<PipelineBoardDto> GetPipelineAsync(Guid currentUserId, OrganizationSelection organization, string? query = null,
        bool includeClosed = false, DateTimeOffset? nowUtc = null, CancellationToken cancellationToken = default);
    OpportunityDetailsDto? GetOpportunity(Guid currentUserId, OrganizationSelection organization, Guid id);
    OpportunityDto CreateOpportunity(Guid currentUserId, OrganizationSelection organization, CreateOpportunityCommand command, DateTimeOffset nowUtc);
    OpportunityDto UpdateOpportunity(Guid currentUserId, OrganizationSelection organization, Guid id, UpdateOpportunityCommand command);
    OpportunityDto AssignOpportunity(Guid currentUserId, OrganizationSelection organization, Guid id, AssignOpportunityCommand command);
    OpportunityDto MoveOpportunity(Guid currentUserId, OrganizationSelection organization, Guid id, MoveOpportunityStageCommand command, DateTimeOffset nowUtc);
    OpportunityActivityDto AddActivity(Guid currentUserId, OrganizationSelection organization, Guid id, AddOpportunityActivityCommand command);
}
