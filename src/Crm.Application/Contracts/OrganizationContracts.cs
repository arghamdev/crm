namespace Crm.Application.Contracts;

public sealed record OrganizationSelection(string CompanyId, string? BranchId, string? TerritoryId);

public sealed record CompanyOptionDto(string Id, string Code, string Name, bool IsSelected);
public sealed record OrganizationUnitOptionDto(string Id, string Code, string Name, string Type, bool IsSelected);
public sealed record TerritoryOptionDto(string Id, string Code, string Name, string Dimension, bool IsSelected);

public sealed record OrganizationContextDto(
    Guid UserId,
    Guid SessionId,
    string? SelectedCompanyId,
    string? SelectedCompanyName,
    string? SelectedCompanyCode,
    string? SelectedBranchId,
    string? SelectedBranchName,
    string? SelectedTerritoryId,
    string? SelectedTerritoryName,
    IReadOnlyList<CompanyOptionDto> Companies,
    IReadOnlyList<OrganizationUnitOptionDto> Branches,
    IReadOnlyList<TerritoryOptionDto> Territories);

public sealed record SelectOrganizationContextCommand(
    string CompanyId,
    string? BranchId,
    string? TerritoryId,
    string? ReturnUrl);

public sealed record OrganizationContextSelectionResult(
    bool Succeeded,
    string Message,
    OrganizationContextDto? Context);
