using Crm.Application.Contracts;

namespace Crm.Application.Services;

public interface IDealerApplicationService
{
    DealerWorkspaceDto GetWorkspace(Guid currentUserId, OrganizationSelection organization, DateTimeOffset nowUtc);
    DealerDetailsDto? Get(Guid currentUserId, OrganizationSelection organization, Guid id, DateTimeOffset nowUtc);
    DealerFormDto GetForm(Guid currentUserId, OrganizationSelection organization, Guid? id, DateTimeOffset nowUtc);
    IReadOnlyList<TerritoryOptionDto> GetTerritoryOptions(Guid currentUserId, OrganizationSelection organization,
        Guid dealerId, DateTimeOffset nowUtc);
    IReadOnlyList<DealerCustomerOptionDto> GetCustomerOptions(Guid currentUserId, OrganizationSelection organization,
        Guid dealerId);
    DealerTargetFormDto GetTargetForm(Guid currentUserId, OrganizationSelection organization,
        Guid dealerId, DateTimeOffset nowUtc);
    DealerDetailsDto Save(Guid currentUserId, OrganizationSelection organization, Guid? id,
        SaveDealerCommand command, DateTimeOffset nowUtc);
    DealerDetailsDto ChangeStatus(Guid currentUserId, OrganizationSelection organization, Guid id,
        ChangeDealerStatusCommand command, DateTimeOffset nowUtc);
    DealerDetailsDto SaveContract(Guid currentUserId, OrganizationSelection organization, Guid dealerId,
        Guid? contractId, SaveDealerContractCommand command, DateTimeOffset nowUtc);
    DealerDetailsDto SubmitContract(Guid currentUserId, OrganizationSelection organization, Guid dealerId,
        Guid contractId, DecideDealerContractCommand command, DateTimeOffset nowUtc);
    DealerDetailsDto ApproveContract(Guid currentUserId, OrganizationSelection organization, Guid dealerId,
        Guid contractId, DecideDealerContractCommand command, DateTimeOffset nowUtc);
    DealerDetailsDto EndContract(Guid currentUserId, OrganizationSelection organization, Guid dealerId,
        Guid contractId, EndDealerRelationshipCommand command, DateTimeOffset nowUtc);
    DealerDetailsDto RequestTerritory(Guid currentUserId, OrganizationSelection organization, Guid dealerId,
        AssignDealerTerritoryCommand command, DateTimeOffset nowUtc);
    DealerDetailsDto ApproveTerritory(Guid currentUserId, OrganizationSelection organization, Guid dealerId,
        Guid assignmentId, DecideDealerTerritoryCommand command, DateTimeOffset nowUtc);
    DealerDetailsDto EndTerritory(Guid currentUserId, OrganizationSelection organization, Guid dealerId,
        Guid assignmentId, EndDealerRelationshipCommand command, DateTimeOffset nowUtc);
    DealerDetailsDto SaveTarget(Guid currentUserId, OrganizationSelection organization, Guid dealerId,
        SaveDealerTargetCommand command, DateTimeOffset nowUtc);
    DealerDetailsDto AssignCustomer(Guid currentUserId, OrganizationSelection organization, Guid dealerId,
        AssignDealerCustomerCommand command, DateTimeOffset nowUtc);
    DealerDetailsDto EndCustomerAssignment(Guid currentUserId, OrganizationSelection organization, Guid dealerId,
        Guid assignmentId, EndDealerRelationshipCommand command, DateTimeOffset nowUtc);
    DealerDetailsDto SyncFinancial(Guid currentUserId, OrganizationSelection organization, Guid dealerId,
        long expectedVersion, DateTimeOffset nowUtc);
    DealerDetailsDto SyncPerformance(Guid currentUserId, OrganizationSelection organization, Guid dealerId,
        long expectedVersion, DateTimeOffset nowUtc);
}
