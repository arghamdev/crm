using Crm.Domain.Customers;

namespace Crm.Application.Contracts;

public sealed record CustomerContactDto(Guid Id, string FullName, string Role, string? Phone, string? Email,
    bool IsPrimary, ContactConsentStatus ConsentStatus, bool IsActive, long Version,
    string? Title = null, string? FirstName = null, string? LastName = null, string? Mobile = null, string? Extension = null, string? Notes = null);
public sealed record CustomerAddressDto(Guid Id, CustomerAddressType Type, string Title, string Province, string City,
    string AddressLine, string? PostalCode, bool IsPrimary, bool IsActive, long Version);
public sealed record CustomerTimelineDto(Guid Id, CustomerTimelineType Type, string Title, string Description,
    DateTimeOffset OccurredAtUtc, string Source, string? SourceReference, Guid? ActorUserId);
public sealed record CustomerOwnershipDto(Guid Id, string BranchId, string? TerritoryId, string Owner,
    DateTimeOffset ValidFromUtc, DateTimeOffset? ValidToUtc, string Reason, Guid ChangedByUserId);
public sealed record DataQualityIssueDto(string Field, string Severity, string Message);
public sealed record CustomerSourceDto(string Section, string SystemOfRecord, DateTimeOffset? LastSynchronizedAtUtc, string FreshnessStatus);
public sealed record CustomerDealerAffiliationDto(Guid DealerId, string DealerCode, string DealerName,
    DateTimeOffset ValidFromUtc, DateTimeOffset? ValidToUtc, bool IsActive);
public sealed record DuplicateCandidateDto(Guid Id, Guid CustomerId, string CustomerCode, string CustomerName,
    Guid PossibleDuplicateCustomerId, string PossibleDuplicateCode, string PossibleDuplicateName,
    int Score, string Reasons, DuplicateReviewStatus Status, DateTimeOffset DetectedAtUtc, long Version,
    long CustomerVersion = 1, long PossibleDuplicateVersion = 1, bool HasActiveMerge = false,
    Guid? ActiveMergeOperationId = null);

public sealed record Customer360Dto(
    CustomerDto Customer,
    IReadOnlyList<CustomerContactDto> Contacts,
    IReadOnlyList<CustomerAddressDto> Addresses,
    IReadOnlyList<CustomerTimelineDto> Timeline,
    IReadOnlyList<CustomerOwnershipDto> OwnershipHistory,
    IReadOnlyList<LeadDto> Leads,
    IReadOnlyList<OpportunityDto> Opportunities,
    IReadOnlyList<QuoteDto> Quotes,
    IReadOnlyList<OrderSummaryDto> Orders,
    IReadOnlyList<CustomerDealerAffiliationDto> DealerAffiliations,
    IReadOnlyList<DataQualityIssueDto> DataQualityIssues,
    IReadOnlyList<CustomerSourceDto> Sources,
    IReadOnlyList<DuplicateCandidateDto> DuplicateCandidates,
    CustomerProfileDto? Profile = null);

public sealed record CustomerEditDto(
    Guid Id, string Code, string Name, string City, string Owner, string BranchId, string? TerritoryId,
    string Segment, CustomerKind Kind, string? NationalId, string? PrimaryPhone, string? PrimaryEmail,
    long ExpectedVersion, IReadOnlyList<OrganizationUnitOptionDto> Branches, IReadOnlyList<TerritoryOptionDto> Territories,
    CustomerProfileInput? Profile = null)
{
    public bool HasLogo { get; init; }
    public CustomerStatus Status { get; init; } = CustomerStatus.Active;
}

public sealed record UpdateCustomerCommand(string Name, string City, string Owner, string BranchId, string? TerritoryId,
    string Segment, string? NationalId, string? PrimaryPhone, string? PrimaryEmail, string OwnershipChangeReason,
    long ExpectedVersion, CustomerProfileInput? Profile = null);
public sealed record AddCustomerContactCommand(string FullName, string Role, string? Phone, string? Email,
    bool IsPrimary, ContactConsentStatus ConsentStatus);
public sealed record AddCustomerAddressCommand(CustomerAddressType Type, string Title, string Province, string City,
    string AddressLine, string? PostalCode, bool IsPrimary);
public sealed record ReviewDuplicateCommand(DuplicateReviewStatus Decision, string Note, long ExpectedVersion);
public sealed record CustomerDuplicateCheckDto(bool HasBlockingExactMatch, IReadOnlyList<DuplicateCandidateDto> Candidates, string Message);
public sealed record CustomerDataQualityRowDto(CustomerDto Customer, IReadOnlyList<DataQualityIssueDto> Issues);
public sealed record CustomerMergePreviewDto(Guid CandidateId, CustomerDto Survivor, CustomerDto Merged,
    int Contacts, int Addresses, int Leads, int Opportunities, int Quotes, int Orders, int DealerAssignments,
    IReadOnlyList<string> Warnings,
    long CandidateVersion, long SurvivorVersion, long MergedVersion);
public sealed record MergeCustomerCommand(Guid SurvivorCustomerId, string Reason, long ExpectedCandidateVersion,
    long ExpectedSurvivorVersion, long ExpectedMergedVersion);
public sealed record UnmergeCustomerCommand(string Reason, long ExpectedVersion);
public sealed record CustomerMergeOperationDto(Guid Id, Guid SurvivorCustomerId, string SurvivorCode, string SurvivorName,
    Guid MergedCustomerId, string MergedCode, string MergedName, CustomerMergeStatus Status, string Reason,
    DateTimeOffset MergedAtUtc, DateTimeOffset? RevertedAtUtc, long Version);
