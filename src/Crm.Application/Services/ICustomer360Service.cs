using Crm.Application.Contracts;

namespace Crm.Application.Services;

public interface ICustomer360Service
{
    Customer360Dto? Get(Guid currentUserId, OrganizationSelection organization, Guid customerId, bool includeRelatedActivity = true);
    CustomerEditDto? GetEdit(Guid currentUserId, OrganizationSelection organization, Guid customerId);
    CustomerDuplicateCheckDto CheckDuplicates(Guid currentUserId, OrganizationSelection organization,
        string name, string city, string? nationalId, string? phone, string? email, Guid? excludeCustomerId = null);
    void Update(Guid currentUserId, OrganizationSelection organization, Guid customerId, UpdateCustomerCommand command, DateTimeOffset nowUtc);
    void AddContact(Guid currentUserId, OrganizationSelection organization, Guid customerId, AddCustomerContactCommand command, DateTimeOffset nowUtc);
    void AddAddress(Guid currentUserId, OrganizationSelection organization, Guid customerId, AddCustomerAddressCommand command, DateTimeOffset nowUtc);
    IReadOnlyList<DuplicateCandidateDto> GetDuplicateReviewQueue(Guid currentUserId, OrganizationSelection organization);
    void ReviewDuplicate(Guid currentUserId, OrganizationSelection organization, Guid candidateId, ReviewDuplicateCommand command, DateTimeOffset nowUtc);
    CustomerMergePreviewDto GetMergePreview(Guid currentUserId, OrganizationSelection organization, Guid candidateId, Guid survivorCustomerId);
    CustomerMergeOperationDto Merge(Guid currentUserId, OrganizationSelection organization, Guid candidateId, MergeCustomerCommand command, DateTimeOffset nowUtc);
    IReadOnlyList<CustomerMergeOperationDto> GetMergeHistory(Guid currentUserId, OrganizationSelection organization);
    void Unmerge(Guid currentUserId, OrganizationSelection organization, Guid operationId, UnmergeCustomerCommand command, DateTimeOffset nowUtc);
    IReadOnlyList<CustomerDataQualityRowDto> GetDataQuality(Guid currentUserId, OrganizationSelection organization);
}
