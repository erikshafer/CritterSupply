using Backoffice.Clients;

namespace Backoffice.E2ETests.Stubs;

/// <summary>
/// Stub implementation of ICatalogClient for E2E tests.
/// Returns in-memory test data configured per scenario.
/// </summary>
public sealed class StubCatalogClient : ICatalogClient
{
    private readonly Dictionary<string, ProductDto> _products = new();

    public void AddProduct(string sku, string name, string description, decimal price)
    {
        _products[sku] = new ProductDto(
            sku,
            name,
            description,
            "General",
            "Active");
    }

    public Task<ProductDto?> GetProductAsync(string sku, CancellationToken ct = default)
    {
        return Task.FromResult(_products.GetValueOrDefault(sku));
    }

    public void Clear()
    {
        _products.Clear();
    }
}
