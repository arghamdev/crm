using Crm.Domain.Organization;

namespace Crm.Application.Contracts;

public sealed record OrganizationCompanyDto(
    Guid Id, string CompanyId, string Code, string Name, string TimeZoneId,
    OrganizationStatus Status, long Version);

public sealed record OrganizationUnitDto(
    Guid Id, string UnitId, string Code, string Name, OrganizationUnitType Type,
    string? ParentUnitId, string? ParentName, OrganizationStatus Status, long Version);

public sealed record TerritoryDto(
    Guid Id, string TerritoryId, string Code, string Name, TerritoryDimension Dimension,
    DateTimeOffset ValidFromUtc, DateTimeOffset? ValidToUtc, OrganizationStatus Status,
    bool IsEffective, long Version);

public sealed record OrganizationChangeDto(
    Guid Id, string EntityType, string EntityBusinessId, string Action, string Summary,
    Guid ActorUserId, DateTimeOffset OccurredAtUtc, string CorrelationId,
    string BeforeValue, string AfterValue);

public sealed record OrganizationAdminDto(
    OrganizationCompanyDto Company,
    IReadOnlyList<OrganizationUnitDto> Units,
    IReadOnlyList<TerritoryDto> Territories,
    IReadOnlyList<OrganizationChangeDto> Changes);

public sealed record OrganizationUnitFormDto(
    string CompanyId, Guid? Id, string UnitId, string Code, string Name,
    OrganizationUnitType Type, string? ParentUnitId, long ExpectedVersion,
    IReadOnlyList<OrganizationUnitOptionDto> ParentOptions);

public sealed record TerritoryFormDto(
    string CompanyId, Guid? Id, string TerritoryId, string Code, string Name,
    TerritoryDimension Dimension, DateTimeOffset ValidFromUtc, DateTimeOffset? ValidToUtc,
    long ExpectedVersion);

public sealed record UpdateCompanyCommand(string Code, string Name, string TimeZoneId, long ExpectedVersion);
public sealed record SaveOrganizationUnitCommand(
    string UnitId, string Code, string Name, OrganizationUnitType Type,
    string? ParentUnitId, long ExpectedVersion);
public sealed record SaveTerritoryCommand(
    string TerritoryId, string Code, string Name, TerritoryDimension Dimension,
    DateTimeOffset ValidFromUtc, DateTimeOffset? ValidToUtc, long ExpectedVersion);

