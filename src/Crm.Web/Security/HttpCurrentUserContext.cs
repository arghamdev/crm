using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Application.Services;

namespace Crm.Web.Security;

public sealed class HttpCurrentUserContext(
    IHttpContextAccessor httpContextAccessor,
    IOrganizationContextService organization) : ICurrentUserContext
{
    private OrganizationContextDto? _organizationContext;
    private HttpContext? HttpContext => httpContextAccessor.HttpContext;
    public bool IsAuthenticated => HttpContext?.User.Identity?.IsAuthenticated == true;
    public Guid CrmUserId => HttpContext?.User.CrmUserId() ?? Guid.Empty;
    public Guid SessionId => HttpContext?.User.CrmSessionId() ?? Guid.Empty;
    private OrganizationContextDto? Current => _organizationContext ??=
        IsAuthenticated && CrmUserId != Guid.Empty && SessionId != Guid.Empty
            ? organization.GetCurrent(CrmUserId, SessionId)
            : null;
    public string? SelectedCompanyId => Current?.SelectedCompanyId;
    public string? SelectedBranchId => Current?.SelectedBranchId;
    public string? SelectedTerritoryId => Current?.SelectedTerritoryId;
}
