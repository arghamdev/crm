namespace Crm.Application.Abstractions;

public sealed record ProductPriceSnapshot(string Code, string Name, string Unit, string CurrencyCode,
    decimal ListUnitPrice, decimal StandardUnitCost, string Source, DateTimeOffset EffectiveAtUtc);

public interface IProductPriceCatalog
{
    IReadOnlyList<ProductPriceSnapshot> GetProducts(string companyId, string currencyCode);
    ProductPriceSnapshot? Find(string companyId, string productCode, string currencyCode);
}
