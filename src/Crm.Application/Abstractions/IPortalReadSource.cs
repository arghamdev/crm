using Crm.Application.Contracts;

namespace Crm.Application.Abstractions;

public interface IPortalReadSource
{
    IReadOnlyList<PortalProductDto> Products(string companyId, string dealerBusinessId, DateTimeOffset now);
    IReadOnlyList<PortalInvoiceDto> Invoices(string companyId, string dealerBusinessId, DateTimeOffset now);
}
