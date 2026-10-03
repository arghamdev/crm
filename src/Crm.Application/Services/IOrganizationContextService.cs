using Crm.Application.Contracts;

namespace Crm.Application.Services;

public interface IUserContextSelector
{
    IReadOnlyList<CompanyOptionDto> GetAvailableCompanies(Guid userId, Guid sessionId);
}

public interface IOrganizationContextService : IUserContextSelector
{
    OrganizationContextDto? GetCurrent(Guid userId, Guid sessionId);
    OrganizationContextDto? GetForSelection(Guid userId, Guid sessionId, string? companyId);
    OrganizationContextSelectionResult Select(
        Guid userId,
        Guid sessionId,
        SelectOrganizationContextCommand command,
        IdentityRequestContext requestContext);
}
