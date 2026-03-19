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

    public Task<bool> UpdateProductDescriptionAsync(string sku, string description, CancellationToken ct = default)
    {
        if (_products.TryGetValue(sku, out var product))
        {
            _products[sku] = product with { Description = description };
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    public Task<bool> UpdateProductDisplayNameAsync(string sku, string displayName, CancellationToken ct = default)
    {
        if (_products.TryGetValue(sku, out var product))
        {
            _products[sku] = product with { Name = displayName };
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    public Task<bool> DiscontinueProductAsync(string sku, CancellationToken ct = default)
    {
        if (_products.TryGetValue(sku, out var product))
        {
            _products[sku] = product with { Status = "Discontinued" };
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    public void Clear()
    {
        _products.Clear();
    }
}
