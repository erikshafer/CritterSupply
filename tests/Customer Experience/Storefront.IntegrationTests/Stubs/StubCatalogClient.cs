using Storefront.Clients;

namespace Storefront.IntegrationTests.Stubs;

/// <summary>
/// Stub implementation of ICatalogClient for testing
/// Returns predefined test data without making real HTTP calls
/// </summary>
public class StubCatalogClient : ICatalogClient
{
    private readonly Dictionary<string, ProductDto> _products = new();

    public void Clear()
    {
        _products.Clear();
    }

    public void AddProduct(ProductDto product)
    {
        _products[product.Sku] = product;
    }

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
        {
            query = query.Where(p => p.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
        }

        var totalCount = query.Count();

        var items = query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return Task.FromResult(new PagedResult<ProductDto>(items, totalCount, page, pageSize));
    }
}
