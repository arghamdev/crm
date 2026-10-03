using Crm.Application.Contracts;

namespace Crm.Application.Services;

public interface IQuoteApplicationService
{
    QuoteWorkspaceDto GetWorkspace(Guid currentUserId, OrganizationSelection organization, DateTimeOffset nowUtc);
    Task<QuoteWorkspaceDto> GetWorkspaceAsync(Guid currentUserId, OrganizationSelection organization, DateTimeOffset nowUtc,
        int page = 1, int pageSize = Crm.Application.Abstractions.PageRequest.DefaultPageSize, CancellationToken cancellationToken = default);
    QuoteDetailsDto? Get(Guid currentUserId, OrganizationSelection organization, Guid id, DateTimeOffset nowUtc);
    IReadOnlyList<ProductPriceDto> GetProducts(string companyId, string currencyCode = "IRR");
    QuoteDetailsDto CreateDraft(Guid currentUserId, OrganizationSelection organization, CreateQuoteDraftCommand command, DateTimeOffset nowUtc);
    QuoteDetailsDto AddLine(Guid currentUserId, OrganizationSelection organization, Guid id, AddQuoteLineCommand command, DateTimeOffset nowUtc);
    QuoteDetailsDto RemoveLine(Guid currentUserId, OrganizationSelection organization, Guid id, Guid lineId, long expectedVersion, DateTimeOffset nowUtc);
    QuoteDetailsDto Submit(Guid currentUserId, OrganizationSelection organization, Guid id, SubmitQuoteCommand command, DateTimeOffset nowUtc);
    QuoteDetailsDto Decide(Guid currentUserId, OrganizationSelection organization, Guid id, DecideQuoteCommand command, DateTimeOffset nowUtc);
    QuoteDetailsDto MarkSent(Guid currentUserId, OrganizationSelection organization, Guid id, long expectedVersion, DateTimeOffset nowUtc);
    QuoteDetailsDto RecordOutcome(Guid currentUserId, OrganizationSelection organization, Guid id, QuoteOutcomeCommand command, DateTimeOffset nowUtc);
    QuoteDetailsDto Revise(Guid currentUserId, OrganizationSelection organization, Guid id, ReviseQuoteCommand command, DateTimeOffset nowUtc);
}
