using Crm.Domain.Common;
using Crm.Domain.Organization;

namespace Crm.Domain.Customers;

public enum CustomerStatus { UnderReview, Active, Inactive }
public enum CustomerKind { Legal, Individual }

public sealed class Customer(
    Guid id,
    string code,
    string name,
    string city,
    string owner,
    string companyId,
    string branchId,
    string branch,
    string? territoryId,
    string segment,
    decimal creditLimit,
    CustomerKind kind = CustomerKind.Legal,
    string? nationalId = null,
    string? primaryPhone = null,
    string? primaryEmail = null,
    string dataSource = "CRM") : Entity(id), IOrganizationScoped
{
    public string Code { get; } = code;
    public string Name { get; private set; } = Require(name, nameof(name));
    public string City { get; private set; } = city;
    public string Owner { get; private set; } = owner;
    public string CompanyId { get; } = Require(companyId, nameof(companyId));
    public string BranchId { get; private set; } = Require(branchId, nameof(branchId));
    public string Branch { get; private set; } = branch;
    public string? TerritoryId { get; private set; } = Optional(territoryId);
    public string Segment { get; private set; } = segment;
    public CustomerStatus Status { get; private set; } = CustomerStatus.UnderReview;
    public decimal CreditLimit { get; private set; } = creditLimit;
    public decimal Balance { get; private set; }
    public CustomerKind Kind { get; } = kind;
    public string? NationalId { get; private set; } = Optional(nationalId);
    public string? PrimaryPhone { get; private set; } = Optional(primaryPhone);
    public string? PrimaryEmail { get; private set; } = NormalizeEmail(primaryEmail);
    public string DataSource { get; private set; } = Require(dataSource, nameof(dataSource));
    public DateTimeOffset? LastSynchronizedAtUtc { get; private set; }

    private Customer() : this(Guid.Empty, "EF", "EF", string.Empty, string.Empty, "EF", "EF", string.Empty, null, string.Empty, 0) { }

    public void Activate() { Status = CustomerStatus.Active; Touch(); }
    public void Deactivate() { Status = CustomerStatus.Inactive; Touch(); }
    public void MergeInto()
    {
        if (Status == CustomerStatus.Inactive) throw new InvalidOperationException("Customer is already inactive.");
        Status = CustomerStatus.Inactive;
        Touch();
    }

    public void RestoreAfterMerge(CustomerStatus previousStatus)
    {
        if (Status != CustomerStatus.Inactive) throw new InvalidOperationException("Only a merged inactive customer can be restored.");
        Status = previousStatus;
        Touch();
    }

    public void UpdateMasterData(
        string name,
        string city,
        string owner,
        string branchId,
        string branch,
        string? territoryId,
        string segment,
        string? nationalId,
        string? primaryPhone,
        string? primaryEmail)
    {
        Name = Require(name, nameof(name));
        City = city?.Trim() ?? string.Empty;
        Owner = owner?.Trim() ?? string.Empty;
        BranchId = Require(branchId, nameof(branchId));
        Branch = Require(branch, nameof(branch));
        TerritoryId = Optional(territoryId);
        Segment = segment?.Trim() ?? string.Empty;
        NationalId = Optional(nationalId);
        PrimaryPhone = Optional(primaryPhone);
        PrimaryEmail = NormalizeEmail(primaryEmail);
        Touch();
    }

    public void MarkSynchronized(string dataSource, DateTimeOffset synchronizedAtUtc)
    {
        DataSource = Require(dataSource, nameof(dataSource));
        LastSynchronizedAtUtc = synchronizedAtUtc;
        Touch();
    }
    public void SetFinancialProjection(decimal balance, decimal creditLimit)
    {
        Balance = Math.Max(0, balance);
        CreditLimit = Math.Max(0, creditLimit);
        Touch();
    }

    private static string Require(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value.Trim();
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? NormalizeEmail(string? value) => Optional(value)?.ToUpperInvariant();
}
