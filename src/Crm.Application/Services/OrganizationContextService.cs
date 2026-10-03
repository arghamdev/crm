using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Domain.Identity;
using Crm.Domain.Organization;

namespace Crm.Application.Services;

public sealed class OrganizationContextService(
    ICrmDataStore store,
    IAccessSnapshotService access) : IOrganizationContextService
{
    public IReadOnlyList<CompanyOptionDto> GetAvailableCompanies(Guid userId, Guid sessionId)
    {
        var snapshot = access.Get(userId);
        if (snapshot is null) return [];
        return store.Read(data =>
        {
            var selected = data.Find<UserSession>(x => x.Id == sessionId && x.CrmUserId == userId).SingleOrDefault()?.SelectedCompanyId;
            var permitted = snapshot.CompanyPermissionSets.Where(x => x.Permissions.Count > 0)
                .Select(x => x.CompanyId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return data.Companies
                .Where(x => x.Status == OrganizationStatus.Active && permitted.Contains(x.CompanyId))
                .OrderBy(x => x.Name)
                .Select(x => new CompanyOptionDto(x.CompanyId, x.Code, x.Name,
                    x.CompanyId.Equals(selected, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        });
    }

    public OrganizationContextDto? GetCurrent(Guid userId, Guid sessionId)
    {
        var snapshot = access.Get(userId);
        if (snapshot is null) return null;
        return store.Read(data => BuildContext(data, snapshot, userId, sessionId));
    }

    public OrganizationContextDto? GetForSelection(Guid userId, Guid sessionId, string? companyId)
    {
        var snapshot = access.Get(userId);
        if (snapshot is null) return null;
        return store.Read(data =>
        {
            var preview = Optional(companyId) ??
                data.Find<UserSession>(x => x.Id == sessionId && x.CrmUserId == userId).SingleOrDefault()?.SelectedCompanyId ??
                data.Companies.Where(x => x.Status == OrganizationStatus.Active && snapshot.PermissionsFor(x.CompanyId).Count > 0)
                    .OrderBy(x => x.Name).Select(x => x.CompanyId).FirstOrDefault();
            return BuildContext(data, snapshot, userId, sessionId, preview);
        });
    }

    public OrganizationContextSelectionResult Select(
        Guid userId,
        Guid sessionId,
        SelectOrganizationContextCommand command,
        IdentityRequestContext requestContext)
    {
        var snapshot = access.Get(userId);
        if (snapshot is null)
            return new OrganizationContextSelectionResult(false, "دسترسی فعال برای انتخاب محیط کاری پیدا نشد.", null);
        return store.Write(data =>
        {
        var session = data.Find<UserSession>(x => x.Id == sessionId && x.CrmUserId == userId).SingleOrDefault();
        if (session is null || session.RevokedAtUtc is not null)
            return new OrganizationContextSelectionResult(false, "نشست معتبر برای انتخاب محیط کاری پیدا نشد.", null);

        var company = data.Companies.SingleOrDefault(x =>
            x.CompanyId.Equals(command.CompanyId?.Trim(), StringComparison.OrdinalIgnoreCase) &&
            x.Status == OrganizationStatus.Active);
        if (company is null || snapshot.PermissionsFor(company.CompanyId).Count == 0)
            return Failed("شرکت انتخاب‌شده در دامنه دسترسی شما نیست.");

        var branchId = Optional(command.BranchId);
        if (branchId is not null)
        {
            var branch = data.OrganizationUnits.SingleOrDefault(x =>
                x.UnitId.Equals(branchId, StringComparison.OrdinalIgnoreCase) &&
                x.CompanyId.Equals(company.CompanyId, StringComparison.OrdinalIgnoreCase) &&
                x.Type == OrganizationUnitType.Branch && x.Status == OrganizationStatus.Active);
            if (branch is null || !CanUseBranch(snapshot, company.CompanyId, branch.UnitId))
                return Failed("شعبه انتخاب‌شده به شرکت یا دامنه دسترسی جاری تعلق ندارد.");
        }

        var territoryId = Optional(command.TerritoryId);
        if (territoryId is not null)
        {
            var territory = data.Territories.SingleOrDefault(x =>
                x.TerritoryId.Equals(territoryId, StringComparison.OrdinalIgnoreCase) &&
                x.CompanyId.Equals(company.CompanyId, StringComparison.OrdinalIgnoreCase) &&
                x.IsEffective(requestContext.NowUtc));
            if (territory is null || !CanUseTerritory(snapshot, company.CompanyId, territory.TerritoryId))
                return Failed("قلمرو انتخاب‌شده به شرکت یا دامنه دسترسی جاری تعلق ندارد.");
        }

        var previous = $"{session.SelectedCompanyId ?? "-"}/{session.SelectedBranchId ?? "*"}/{session.SelectedTerritoryId ?? "*"}";
        session.SelectOrganizationContext(company.CompanyId, branchId, territoryId);
        data.Append<Crm.Domain.Identity.SecurityAuditEvent>(new SecurityAuditEvent(Guid.NewGuid(), requestContext.NowUtc,
            "UserContext.CompanyChanged", "Success", userId, userId, sessionId, requestContext.CorrelationId,
            $"{previous}->{company.CompanyId}/{branchId ?? "*"}/{territoryId ?? "*"}",
            requestContext.IpHash, requestContext.UserAgentSummary));
        return new OrganizationContextSelectionResult(true, "محیط کاری تغییر کرد.",
            BuildContext(data, snapshot, userId, sessionId));

        OrganizationContextSelectionResult Failed(string message)
        {
            data.Append<Crm.Domain.Identity.SecurityAuditEvent>(new SecurityAuditEvent(Guid.NewGuid(), requestContext.NowUtc,
                "UserContext.CompanyChanged", "Failure", userId, userId, sessionId, requestContext.CorrelationId,
                message, requestContext.IpHash, requestContext.UserAgentSummary));
            return new OrganizationContextSelectionResult(false, message, BuildContext(data, snapshot, userId, sessionId));
        }
        });
    }

    private static OrganizationContextDto BuildContext(
        CrmDataSet data,
        AccessSnapshot snapshot,
        Guid userId,
        Guid sessionId,
        string? previewCompanyId = null)
    {
        var session = data.Find<UserSession>(x => x.Id == sessionId && x.CrmUserId == userId).SingleOrDefault();
        var selectedCompanyId = previewCompanyId ?? session?.SelectedCompanyId;
        var permittedCompanyIds = snapshot.CompanyPermissionSets.Where(x => x.Permissions.Count > 0)
            .Select(x => x.CompanyId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var companies = data.Companies
            .Where(x => x.Status == OrganizationStatus.Active && permittedCompanyIds.Contains(x.CompanyId))
            .OrderBy(x => x.Name)
            .Select(x => new CompanyOptionDto(x.CompanyId, x.Code, x.Name,
                x.CompanyId.Equals(selectedCompanyId, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var selectedCompany = data.Companies.SingleOrDefault(x =>
            selectedCompanyId is not null && x.CompanyId.Equals(selectedCompanyId, StringComparison.OrdinalIgnoreCase) &&
            x.Status == OrganizationStatus.Active && permittedCompanyIds.Contains(x.CompanyId));
        var sessionMatchesCompany = selectedCompany is not null &&
            string.Equals(session?.SelectedCompanyId, selectedCompany.CompanyId, StringComparison.OrdinalIgnoreCase);
        var activeBranchId = sessionMatchesCompany ? session?.SelectedBranchId : null;
        var activeTerritoryId = sessionMatchesCompany ? session?.SelectedTerritoryId : null;

        var branches = selectedCompany is null ? [] : data.OrganizationUnits
            .Where(x => x.CompanyId.Equals(selectedCompany.CompanyId, StringComparison.OrdinalIgnoreCase) &&
                x.Type == OrganizationUnitType.Branch && x.Status == OrganizationStatus.Active &&
                CanUseBranch(snapshot, selectedCompany.CompanyId, x.UnitId))
            .OrderBy(x => x.Name)
            .Select(x => new OrganizationUnitOptionDto(x.UnitId, x.Code, x.Name, x.Type.ToString(),
                x.UnitId.Equals(activeBranchId, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var territories = selectedCompany is null ? [] : data.Territories
            .Where(x => x.CompanyId.Equals(selectedCompany.CompanyId, StringComparison.OrdinalIgnoreCase) &&
                x.Status == OrganizationStatus.Active && CanUseTerritory(snapshot, selectedCompany.CompanyId, x.TerritoryId))
            .OrderBy(x => x.Name)
            .Select(x => new TerritoryOptionDto(x.TerritoryId, x.Code, x.Name, x.Dimension.ToString(),
                x.TerritoryId.Equals(activeTerritoryId, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var selectedBranch = branches.SingleOrDefault(x => x.IsSelected);
        var selectedTerritory = territories.SingleOrDefault(x => x.IsSelected);
        return new OrganizationContextDto(userId, sessionId, selectedCompany?.CompanyId, selectedCompany?.Name,
            selectedCompany?.Code, selectedBranch?.Id, selectedBranch?.Name, selectedTerritory?.Id,
            selectedTerritory?.Name, companies, branches, territories);
    }

    private static bool CanUseBranch(AccessSnapshot snapshot, string companyId, string branchId) =>
        snapshot.PermissionScopeGrants.Any(x =>
            x.CompanyId.Equals(companyId, StringComparison.OrdinalIgnoreCase) &&
            (x.ScopeType.Equals("Company", StringComparison.OrdinalIgnoreCase) ||
             x.ScopeType.Equals("Branch", StringComparison.OrdinalIgnoreCase) &&
             x.ScopeId.Equals(branchId, StringComparison.OrdinalIgnoreCase) ||
             x.ScopeType.Equals("Territory", StringComparison.OrdinalIgnoreCase)));

    private static bool CanUseTerritory(AccessSnapshot snapshot, string companyId, string territoryId) =>
        snapshot.PermissionScopeGrants.Any(x =>
            x.CompanyId.Equals(companyId, StringComparison.OrdinalIgnoreCase) &&
            (x.ScopeType.Equals("Company", StringComparison.OrdinalIgnoreCase) ||
             x.ScopeType.Equals("Territory", StringComparison.OrdinalIgnoreCase) &&
             x.ScopeId.Equals(territoryId, StringComparison.OrdinalIgnoreCase)));

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
