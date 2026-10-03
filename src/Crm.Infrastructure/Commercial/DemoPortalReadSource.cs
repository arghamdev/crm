using Crm.Application.Abstractions;
using Crm.Application.Contracts;

namespace Crm.Infrastructure.Commercial;

public sealed class DemoPortalReadSource(IProductPriceCatalog catalog) : IPortalReadSource
{
    // Explicit account mapping; never return another dealer's commercial records as a fallback.
    public IReadOnlyList<PortalProductDto> Products(string companyId, string businessDealerId, DateTimeOffset now) =>
        companyId == "C01" && businessDealerId == "P-D01" ? catalog.GetProducts(companyId, "IRR").Where(x => x.Code.StartsWith("PRD-", StringComparison.Ordinal)).Select((x, n) =>
            new PortalProductDto(x.Code, x.Name, x.Unit, x.ListUnitPrice, 20 - n * 5, "Demo ERP / قیمت پایه؛ تخفیف کمپین ندارد", now.AddMinutes(-5))).ToList() : [];
    public IReadOnlyList<PortalInvoiceDto> Invoices(string companyId, string businessDealerId, DateTimeOffset now) =>
        companyId == "C01" && businessDealerId == "P-D01" ? [new("D01-INV-01", "DEMO-1405-001", 1200000000m, 580000000m, now.AddDays(-7), "Demo ERP / حساب نماینده", now.AddMinutes(-15))] : [];
}
