using Crm.Domain.Common;

namespace Crm.Domain.Customers;

public enum ContactConsentStatus { Unknown, Granted, Revoked }
public enum CustomerAddressType { Registered, Billing, Shipping, Visit }
public enum CustomerTimelineType
{
    Created,
    MasterDataChanged,
    ContactAdded,
    AddressAdded,
    OwnershipChanged,
    FinancialSync,
    DuplicateReview,
    Merge,
    Unmerge,
    LeadConverted,
    OpportunityChanged,
    VisitCompleted
}
public enum DuplicateReviewStatus { Pending, Confirmed, Dismissed }
public enum CustomerMergeStatus { Merged, Reverted }

public sealed class CustomerContact(
    Guid id,
    string companyId,
    Guid customerId,
    string fullName,
    string role,
    string? phone,
    string? email,
    bool isPrimary,
    ContactConsentStatus consentStatus) : Entity(id)
{
    public string CompanyId { get; } = Required(companyId, nameof(companyId));
    public Guid CustomerId { get; private set; } = customerId;
    public string FullName { get; private set; } = Required(fullName, nameof(fullName));
    public string Role { get; private set; } = role?.Trim() ?? string.Empty;
    public string? Phone { get; private set; } = Optional(phone);
    public string? Email { get; private set; } = NormalizeEmail(email);
    public bool IsPrimary { get; private set; } = isPrimary;
    public ContactConsentStatus ConsentStatus { get; private set; } = consentStatus;
    public bool IsActive { get; private set; } = true;

    private CustomerContact() : this(Guid.Empty, "EF", Guid.Empty, "EF", string.Empty, null, null, false, ContactConsentStatus.Unknown) { }

    public void Update(string fullName, string role, string? phone, string? email, bool isPrimary, ContactConsentStatus consentStatus)
    {
        FullName = Required(fullName, nameof(fullName));
        Role = role?.Trim() ?? string.Empty;
        Phone = Optional(phone);
        Email = NormalizeEmail(email);
        IsPrimary = isPrimary;
        ConsentStatus = consentStatus;
        Touch();
    }

    public void Deactivate() { IsActive = false; Touch(); }
    public void ReassignCustomer(Guid customerId)
    {
        if (customerId == Guid.Empty) throw new ArgumentException("CustomerId is required.", nameof(customerId));
        CustomerId = customerId;
        Touch();
    }
    public void SetPrimary(bool isPrimary) { IsPrimary = isPrimary; Touch(); }

    private static string Required(string value, string name) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value.Trim();
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? NormalizeEmail(string? value) => Optional(value)?.ToUpperInvariant();
}

public sealed class CustomerAddress(
    Guid id,
    string companyId,
    Guid customerId,
    CustomerAddressType type,
    string title,
    string province,
    string city,
    string addressLine,
    string? postalCode,
    bool isPrimary) : Entity(id)
{
    public string CompanyId { get; } = Required(companyId, nameof(companyId));
    public Guid CustomerId { get; private set; } = customerId;
    public CustomerAddressType Type { get; } = type;
    public string Title { get; private set; } = Required(title, nameof(title));
    public string Province { get; private set; } = province?.Trim() ?? string.Empty;
    public string City { get; private set; } = city?.Trim() ?? string.Empty;
    public string AddressLine { get; private set; } = Required(addressLine, nameof(addressLine));
    public string? PostalCode { get; private set; } = Optional(postalCode);
    public bool IsPrimary { get; private set; } = isPrimary;
    public bool IsActive { get; private set; } = true;

    private CustomerAddress() : this(Guid.Empty, "EF", Guid.Empty, CustomerAddressType.Registered, "EF", string.Empty, string.Empty, "EF", null, false) { }

    public void Update(string title, string province, string city, string addressLine, string? postalCode, bool isPrimary)
    {
        Title = Required(title, nameof(title));
        Province = province?.Trim() ?? string.Empty;
        City = city?.Trim() ?? string.Empty;
        AddressLine = Required(addressLine, nameof(addressLine));
        PostalCode = Optional(postalCode);
        IsPrimary = isPrimary;
        Touch();
    }

    public void Deactivate() { IsActive = false; Touch(); }
    public void ReassignCustomer(Guid customerId)
    {
        if (customerId == Guid.Empty) throw new ArgumentException("CustomerId is required.", nameof(customerId));
        CustomerId = customerId;
        Touch();
    }
    public void SetPrimary(bool isPrimary) { IsPrimary = isPrimary; Touch(); }

    private static string Required(string value, string name) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value.Trim();
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class CustomerTimelineEvent(
    Guid id,
    string companyId,
    Guid customerId,
    CustomerTimelineType type,
    string title,
    string description,
    DateTimeOffset occurredAtUtc,
    string source,
    string? sourceReference,
    Guid? actorUserId) : Entity(id)
{
    public string CompanyId { get; } = Required(companyId, nameof(companyId));
    public Guid CustomerId { get; } = customerId;
    public CustomerTimelineType Type { get; } = type;
    public string Title { get; } = Required(title, nameof(title));
    public string Description { get; } = description?.Trim() ?? string.Empty;
    public DateTimeOffset OccurredAtUtc { get; } = occurredAtUtc;
    public string Source { get; } = Required(source, nameof(source));
    public string? SourceReference { get; } = Optional(sourceReference);
    public Guid? ActorUserId { get; } = actorUserId;

    private CustomerTimelineEvent() : this(Guid.Empty, "EF", Guid.Empty, CustomerTimelineType.Created, "EF", string.Empty,
        DateTimeOffset.MinValue, "EF", null, null) { }

    private static string Required(string value, string name) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value.Trim();
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class CustomerOwnershipHistory(
    Guid id,
    string companyId,
    Guid customerId,
    string branchId,
    string? territoryId,
    string owner,
    DateTimeOffset validFromUtc,
    string reason,
    Guid changedByUserId) : Entity(id)
{
    public string CompanyId { get; } = Required(companyId, nameof(companyId));
    public Guid CustomerId { get; } = customerId;
    public string BranchId { get; } = Required(branchId, nameof(branchId));
    public string? TerritoryId { get; } = Optional(territoryId);
    public string Owner { get; } = Required(owner, nameof(owner));
    public DateTimeOffset ValidFromUtc { get; } = validFromUtc;
    public DateTimeOffset? ValidToUtc { get; private set; }
    public string Reason { get; } = Required(reason, nameof(reason));
    public Guid ChangedByUserId { get; } = changedByUserId;

    private CustomerOwnershipHistory() : this(Guid.Empty, "EF", Guid.Empty, "EF", null, "EF", DateTimeOffset.MinValue, "EF", Guid.Empty) { }

    public void Close(DateTimeOffset validToUtc)
    {
        if (validToUtc <= ValidFromUtc) throw new InvalidOperationException("Ownership end must be after start.");
        ValidToUtc = validToUtc;
        Touch();
    }

    private static string Required(string value, string name) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value.Trim();
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class CustomerDuplicateCandidate(
    Guid id,
    string companyId,
    Guid customerId,
    Guid possibleDuplicateCustomerId,
    int score,
    string reasons,
    DateTimeOffset detectedAtUtc) : Entity(id)
{
    public string CompanyId { get; } = Required(companyId, nameof(companyId));
    public Guid CustomerId { get; } = customerId;
    public Guid PossibleDuplicateCustomerId { get; } = possibleDuplicateCustomerId;
    public int Score { get; } = Math.Clamp(score, 0, 100);
    public string Reasons { get; } = Required(reasons, nameof(reasons));
    public DateTimeOffset DetectedAtUtc { get; } = detectedAtUtc;
    public DuplicateReviewStatus Status { get; private set; } = DuplicateReviewStatus.Pending;
    public Guid? ReviewedByUserId { get; private set; }
    public DateTimeOffset? ReviewedAtUtc { get; private set; }
    public string? ReviewNote { get; private set; }

    private CustomerDuplicateCandidate() : this(Guid.Empty, "EF", Guid.Empty, Guid.NewGuid(), 0, "EF", DateTimeOffset.MinValue) { }

    private void EnsureValidCustomers()
    {
        if (CustomerId == Guid.Empty || PossibleDuplicateCustomerId == Guid.Empty || CustomerId == PossibleDuplicateCustomerId)
            throw new InvalidOperationException("Duplicate candidate must reference two different customers.");
    }

    public void Review(DuplicateReviewStatus status, Guid reviewedByUserId, DateTimeOffset reviewedAtUtc, string note)
    {
        EnsureValidCustomers();
        if (status == DuplicateReviewStatus.Pending) throw new InvalidOperationException("Review must resolve the candidate.");
        if (Status != DuplicateReviewStatus.Pending) throw new InvalidOperationException("Duplicate candidate is already resolved.");
        if (string.IsNullOrWhiteSpace(note)) throw new InvalidOperationException("یادداشت تصمیم الزامی است.");
        Status = status;
        ReviewedByUserId = reviewedByUserId;
        ReviewedAtUtc = reviewedAtUtc;
        ReviewNote = note.Trim();
        Touch();
    }

    private static string Required(string value, string name) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value.Trim();
}

public sealed class CustomerMergeOperation(
    Guid id,
    string companyId,
    Guid duplicateCandidateId,
    Guid survivorCustomerId,
    Guid mergedCustomerId,
    CustomerStatus mergedCustomerPreviousStatus,
    string transferManifestJson,
    string reason,
    Guid mergedByUserId,
    DateTimeOffset mergedAtUtc) : Entity(id)
{
    public string CompanyId { get; } = Required(companyId, nameof(companyId));
    public Guid DuplicateCandidateId { get; } = duplicateCandidateId;
    public Guid SurvivorCustomerId { get; } = survivorCustomerId;
    public Guid MergedCustomerId { get; } = mergedCustomerId;
    public CustomerStatus MergedCustomerPreviousStatus { get; } = mergedCustomerPreviousStatus;
    public string TransferManifestJson { get; } = Required(transferManifestJson, nameof(transferManifestJson));
    public string Reason { get; } = Required(reason, nameof(reason));
    public Guid MergedByUserId { get; } = mergedByUserId;
    public DateTimeOffset MergedAtUtc { get; } = mergedAtUtc;
    public CustomerMergeStatus Status { get; private set; } = CustomerMergeStatus.Merged;
    public Guid? RevertedByUserId { get; private set; }
    public DateTimeOffset? RevertedAtUtc { get; private set; }
    public string? RevertReason { get; private set; }

    private CustomerMergeOperation() : this(Guid.Empty, "EF", Guid.Empty, Guid.Empty, Guid.Empty,
        CustomerStatus.UnderReview, "{}", "EF", Guid.Empty, DateTimeOffset.MinValue) { }

    public void Revert(Guid userId, DateTimeOffset atUtc, string reason)
    {
        if (SurvivorCustomerId == Guid.Empty || MergedCustomerId == Guid.Empty || SurvivorCustomerId == MergedCustomerId)
            throw new InvalidOperationException("Merge operation customer references are invalid.");
        if (Status != CustomerMergeStatus.Merged) throw new InvalidOperationException("Merge operation is already reverted.");
        Status = CustomerMergeStatus.Reverted;
        RevertedByUserId = userId;
        RevertedAtUtc = atUtc;
        RevertReason = Required(reason, nameof(reason));
        Touch();
    }

    private static string Required(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value.Trim();
}
