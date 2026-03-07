using Storefront.Clients;

namespace Storefront.E2ETests.Stubs;

/// <summary>
/// Stub implementation of ICatalogClient for E2E testing.
/// Returns predefined product data without making real HTTP calls.
/// </summary>
public sealed class StubCatalogClient : ICatalogClient
{
    private readonly Dictionary<string, ProductDto> _products = new();

    public void AddProduct(ProductDto product) => _products[product.Sku] = product;

    public Task<ProductDto?> GetProductAsync(string sku, CancellationToken ct = default)
    {
        _products.TryGetValue(sku, out var product);
        return Task.FromResult(product);
    }

    public Task<PagedResult<ProductDto>> GetProductsAsync(
        string? category = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        var query = _products.Values.AsEnumerable();
        if (!string.IsNullOrEmpty(category))
            query = query.Where(p => p.Category.Equals(category, StringComparison.OrdinalIgnoreCase));

        var all = query.ToList();
        var items = all.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Task.FromResult(new PagedResult<ProductDto>(items, all.Count, page, pageSize));
    }

    public void Clear() => _products.Clear();
}
