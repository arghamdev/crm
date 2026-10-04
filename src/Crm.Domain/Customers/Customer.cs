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
    /// <summary>Customer, prospect, supplier or partner.</summary>
    public Crm.Domain.Accounts.AccountRelationship RelationshipType { get; private set; } = Crm.Domain.Accounts.AccountRelationship.Customer;
    /// <summary>Comma-separated labels (max 10, 30 characters each).</summary>
    public string? Tags { get; private set; }
    /// <summary>Parent account in a group hierarchy (holding/subsidiary).</summary>
    public Guid? ParentCustomerId { get; private set; }

    public IReadOnlyList<string> TagList => Tags?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [];

    public void Classify(Crm.Domain.Accounts.AccountRelationship relationship, IEnumerable<string>? tags)
    {
        if (!Enum.IsDefined(relationship)) throw new InvalidOperationException("نوع رابطه معتبر نیست.");
        var list = (tags ?? []).Select(x => Common.PersianText.NormalizeLetters(x)).Where(x => x is not null).Select(x => x!.Replace(",", " "))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (list.Count > 10) throw new InvalidOperationException("حداکثر ۱۰ برچسب مجاز است.");
        if (list.Any(x => x.Length > 30)) throw new InvalidOperationException("هر برچسب حداکثر ۳۰ نویسه است.");
        RelationshipType = relationship;
        Tags = list.Count == 0 ? null : string.Join(',', list);
        Touch();
    }

    /// <summary>Links (or with null detaches) the parent account; cycles are checked by the caller against the hierarchy.</summary>
    public void SetParent(Guid? parentCustomerId)
    {
        if (parentCustomerId == Id) throw new InvalidOperationException("حساب نمی‌تواند مادر خودش باشد.");
        ParentCustomerId = parentCustomerId;
        Touch();
    }

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
