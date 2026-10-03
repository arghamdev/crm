using Crm.Application.Contracts;
using Crm.Domain.Organization;

namespace Crm.Application.Services;

public interface IOrganizationAdminService
{
    OrganizationAdminDto Get(string companyId, Guid actorUserId, DateTimeOffset nowUtc);
    UpdateCompanyCommand GetCompanyForm(string companyId, Guid actorUserId);
    OrganizationUnitFormDto GetUnitForm(string companyId, Guid? id, OrganizationUnitType? type, Guid actorUserId);
    TerritoryFormDto GetTerritoryForm(string companyId, Guid? id, Guid actorUserId, DateTimeOffset nowUtc);
    void UpdateCompany(string companyId, UpdateCompanyCommand command, Guid actorUserId, IdentityRequestContext context);
    void SaveUnit(string companyId, Guid? id, SaveOrganizationUnitCommand command, Guid actorUserId, IdentityRequestContext context);
    void SetUnitStatus(string companyId, Guid id, OrganizationStatus status, long expectedVersion, Guid actorUserId, IdentityRequestContext context);
    void SaveTerritory(string companyId, Guid? id, SaveTerritoryCommand command, Guid actorUserId, IdentityRequestContext context);
    void SetTerritoryStatus(string companyId, Guid id, OrganizationStatus status, long expectedVersion, Guid actorUserId, IdentityRequestContext context);
}

