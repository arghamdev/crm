using Crm.Domain.Common;

namespace Crm.Domain.Organization;

public enum OrganizationStatus { Active, Inactive }
public enum OrganizationUnitType { Headquarters, Region, Branch, SalesTeam }
public enum TerritoryDimension { Geography, Product, Industry, Channel }

public interface IOrganizationScoped
{
    string CompanyId { get; }
    string BranchId { get; }
    string? TerritoryId { get; }
}

public sealed class Company(
    Guid id,
    string companyId,
    string code,
    string name,
    string timeZoneId = "Asia/Tehran") : Entity(id)
{
    public string CompanyId { get; } = Required(companyId, nameof(companyId));
    public string Code { get; private set; } = Required(code, nameof(code));
    public string Name { get; private set; } = Required(name, nameof(name));
    public string TimeZoneId { get; private set; } = Required(timeZoneId, nameof(timeZoneId));
    public OrganizationStatus Status { get; private set; } = OrganizationStatus.Active;

    private Company() : this(Guid.Empty, "EF", "EF", "EF") { }

    public void Update(string code, string name, string timeZoneId)
    {
        Code = Required(code, nameof(code));
        Name = Required(name, nameof(name));
        TimeZoneId = Required(timeZoneId, nameof(timeZoneId));
        Touch();
    }

    public void SetStatus(OrganizationStatus status)
    {
        if (Status == status) return;
        Status = status;
        Touch();
    }

    private static string Required(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value.Trim();
}

public sealed class OrganizationUnit(
    Guid id,
    string unitId,
    string companyId,
    string code,
    string name,
    OrganizationUnitType type,
    string? parentUnitId = null) : Entity(id)
{
    public string UnitId { get; } = Required(unitId, nameof(unitId));
    public string CompanyId { get; } = Required(companyId, nameof(companyId));
    public string Code { get; private set; } = Required(code, nameof(code));
    public string Name { get; private set; } = Required(name, nameof(name));
    public OrganizationUnitType Type { get; } = type;
    public string? ParentUnitId { get; private set; } = string.IsNullOrWhiteSpace(parentUnitId) ? null : parentUnitId.Trim();
    public OrganizationStatus Status { get; private set; } = OrganizationStatus.Active;

    private OrganizationUnit() : this(Guid.Empty, "EF", "EF", "EF", "EF", OrganizationUnitType.Branch) { }

    public void Update(string code, string name, string? parentUnitId)
    {
        Code = Required(code, nameof(code));
        Name = Required(name, nameof(name));
        ParentUnitId = string.IsNullOrWhiteSpace(parentUnitId) ? null : parentUnitId.Trim();
        Touch();
    }

    public void SetStatus(OrganizationStatus status)
    {
        if (Status == status) return;
        Status = status;
        Touch();
    }

    private static string Required(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value.Trim();
}

public sealed class Territory(
    Guid id,
    string territoryId,
    string companyId,
    string code,
    string name,
    TerritoryDimension dimension,
    DateTimeOffset validFromUtc,
    DateTimeOffset? validToUtc = null) : Entity(id)
{
    public string TerritoryId { get; } = Required(territoryId, nameof(territoryId));
    public string CompanyId { get; } = Required(companyId, nameof(companyId));
    public string Code { get; private set; } = Required(code, nameof(code));
    public string Name { get; private set; } = Required(name, nameof(name));
    public TerritoryDimension Dimension { get; } = dimension;
    public DateTimeOffset ValidFromUtc { get; private set; } = ValidWindow(validFromUtc, validToUtc).From;
    public DateTimeOffset? ValidToUtc { get; private set; } = ValidWindow(validFromUtc, validToUtc).To;
    public OrganizationStatus Status { get; private set; } = OrganizationStatus.Active;

    private Territory() : this(Guid.Empty, "EF", "EF", "EF", "EF", TerritoryDimension.Geography, DateTimeOffset.MinValue) { }

    public bool IsEffective(DateTimeOffset nowUtc) => Status == OrganizationStatus.Active &&
        ValidFromUtc <= nowUtc && (ValidToUtc is not { } endUtc || nowUtc < endUtc);

    public void Update(string code, string name, DateTimeOffset validFromUtc, DateTimeOffset? validToUtc)
    {
        var window = ValidWindow(validFromUtc, validToUtc);
        Code = Required(code, nameof(code));
        Name = Required(name, nameof(name));
        ValidFromUtc = window.From;
        ValidToUtc = window.To;
        Touch();
    }

    public void SetStatus(OrganizationStatus status)
    {
        if (Status == status) return;
        Status = status;
        Touch();
    }

    private static (DateTimeOffset From, DateTimeOffset? To) ValidWindow(DateTimeOffset from, DateTimeOffset? to)
    {
        if (to is not null && to <= from)
            throw new ArgumentException("Validity end must be later than validity start.", nameof(to));
        return (from, to);
    }

    private static string Required(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value.Trim();
}

public sealed class OrganizationChange(
    Guid id,
    string companyId,
    string entityType,
    string entityBusinessId,
    string action,
    string summary,
    Guid actorUserId,
    DateTimeOffset occurredAtUtc,
    string correlationId,
    string beforeValue,
    string afterValue) : Entity(id)
{
    public string CompanyId { get; } = Required(companyId, nameof(companyId));
    public string EntityType { get; } = Required(entityType, nameof(entityType));
    public string EntityBusinessId { get; } = Required(entityBusinessId, nameof(entityBusinessId));
    public string Action { get; } = Required(action, nameof(action));
    public string Summary { get; } = Required(summary, nameof(summary));
    public Guid ActorUserId { get; } = actorUserId;
    public DateTimeOffset OccurredAtUtc { get; } = occurredAtUtc;
    public string CorrelationId { get; } = Required(correlationId, nameof(correlationId));
    public string BeforeValue { get; } = beforeValue ?? string.Empty;
    public string AfterValue { get; } = afterValue ?? string.Empty;

    private OrganizationChange() : this(Guid.Empty, "EF", "EF", "EF", "EF", "EF", Guid.Empty,
        DateTimeOffset.MinValue, "EF", string.Empty, string.Empty) { }

    private static string Required(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value.Trim();
}
