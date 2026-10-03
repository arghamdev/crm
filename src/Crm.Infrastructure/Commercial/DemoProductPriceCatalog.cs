using Crm.Application.Abstractions;

namespace Crm.Infrastructure.Commercial;

public sealed class DemoProductPriceCatalog : IProductPriceCatalog
{
    private static readonly DateTimeOffset EffectiveAt = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly IReadOnlyList<ProductPriceSnapshot> Products =
    [
        new("PRD-1001", "بسته مواد اولیه استاندارد", "بسته", "IRR", 850_000_000m, 610_000_000m, "ERP Mock / PriceBook 1405-06", EffectiveAt),
        new("PRD-1002", "بسته مواد اولیه ویژه", "بسته", "IRR", 1_250_000_000m, 880_000_000m, "ERP Mock / PriceBook 1405-06", EffectiveAt),
        new("SRV-2001", "خدمات استقرار و آموزش", "پروژه", "IRR", 420_000_000m, 250_000_000m, "ERP Mock / Service Tariff 1405", EffectiveAt),
        new("SRV-2002", "پشتیبانی سالانه", "سال", "IRR", 680_000_000m, 390_000_000m, "ERP Mock / Service Tariff 1405", EffectiveAt)
    ];

    public IReadOnlyList<ProductPriceSnapshot> GetProducts(string companyId, string currencyCode) => Products
        .Where(x => x.CurrencyCode.Equals(currencyCode, StringComparison.OrdinalIgnoreCase)).ToArray();

    public ProductPriceSnapshot? Find(string companyId, string productCode, string currencyCode) => Products.SingleOrDefault(x =>
        x.Code.Equals(productCode?.Trim(), StringComparison.OrdinalIgnoreCase) &&
        x.CurrencyCode.Equals(currencyCode, StringComparison.OrdinalIgnoreCase));
}
