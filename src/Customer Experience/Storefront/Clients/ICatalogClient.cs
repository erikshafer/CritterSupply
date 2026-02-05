namespace Storefront.Clients;

/// <summary>
/// HTTP client for querying Product Catalog BC
/// </summary>
public interface ICatalogClient
{
    Task<ProductDto?> GetProductAsync(string sku, CancellationToken ct = default);
    Task<PagedResult<ProductDto>> GetProductsAsync(
        string? category = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default);
}

/// <summary>
/// Product DTO from Catalog BC
/// </summary>
public sealed record ProductDto(
    string Sku,
    string Name,
    string Description,
    string Category,
    decimal Price,
    string Status,
    IReadOnlyList<ProductImageDto> Images);

/// <summary>
/// Product image DTO from Catalog BC
/// </summary>
public sealed record ProductImageDto(
    string Url,
    string AltText,
    int SortOrder);

/// <summary>
/// Paged result wrapper
/// </summary>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize);
