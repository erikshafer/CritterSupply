namespace Backoffice.Clients;

/// <summary>
/// HTTP client for querying Product Catalog BC (admin use)
/// </summary>
public interface ICatalogClient
{
    /// <summary>
    /// Get product details by SKU (CS workflow: product info lookup)
    /// </summary>
    Task<ProductDto?> GetProductAsync(string sku, CancellationToken ct = default);
}

/// <summary>
/// Product DTO from Product Catalog BC
/// </summary>
public sealed record ProductDto(
    string Sku,
    string Name,
    string Description,
    string Category,
    string Status);
