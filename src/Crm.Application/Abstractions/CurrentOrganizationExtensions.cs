using Crm.Application.Contracts;

namespace Crm.Application.Abstractions;

public static class CurrentOrganizationExtensions
{
    public static OrganizationSelection RequiredOrganization(this ICurrentUserContext current)
    {
        if (!current.IsAuthenticated || current.SelectedCompanyId is null)
            throw new UnauthorizedAccessException("An organization context must be selected.");
        return new OrganizationSelection(current.SelectedCompanyId, current.SelectedBranchId, current.SelectedTerritoryId);
    }
}
