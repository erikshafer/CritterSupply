using Storefront.Clients;
using System.Net.Http.Json;
using System.Text.Json;

namespace Storefront.Api.Clients;

public sealed class CatalogClient : ICatalogClient
{
    private readonly HttpClient _httpClient;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public CatalogClient(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient("CatalogClient");
    }

    public async Task<ProductDto?> GetProductAsync(string sku, CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync($"/api/products/{sku}", ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        var catalogProduct = await response.Content.ReadFromJsonAsync<CatalogProductResponse>(JsonOptions, ct);
        if (catalogProduct is null)
            return null;

        return MapToProductDto(catalogProduct);
    }

    public async Task<PagedResult<ProductDto>> GetProductsAsync(
        string? category = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        var url = $"/api/products?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrEmpty(category))
            url += $"&category={Uri.EscapeDataString(category)}";

        var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var catalogResponse = await response.Content.ReadFromJsonAsync<CatalogProductListResponse>(JsonOptions, ct);
        if (catalogResponse is null)
            return new PagedResult<ProductDto>([], 0, page, pageSize);

        var products = catalogResponse.Products
            .Select(MapToProductDto)
            .ToList();

        return new PagedResult<ProductDto>(products, catalogResponse.TotalCount, catalogResponse.Page, catalogResponse.PageSize);
    }

    private static ProductDto MapToProductDto(CatalogProductResponse product)
    {
        return new ProductDto(
            Sku: product.Sku ?? product.Id ?? string.Empty,
            Name: product.Name ?? "Unknown Product",
            Description: product.Description ?? string.Empty,
            Category: product.Category ?? "Uncategorized",
            Price: 0m, // TODO: Price will come from Pricing BC in future cycle
            Status: product.Status?.ToString() ?? "Unknown",
            Images: product.Images?.Select(i => new ProductImageDto(i.Url, i.AltText, i.SortOrder)).ToList() ?? []);
    }

    // Response types matching Product Catalog BC API
    private sealed record CatalogProductListResponse(
        IReadOnlyList<CatalogProductResponse> Products,
        int Page,
        int PageSize,
        int TotalCount);

    private sealed record CatalogProductResponse(
        string? Id,
        string? Sku,            // Product Catalog returns plain string, not value object
        string? Name,           // Product Catalog returns plain string, not value object
        string? Description,
        string? Category,
        int? Status,            // Product Catalog returns integer enum (0 = Active, etc.)
        IReadOnlyList<CatalogProductImage>? Images);

    private sealed record CatalogProductImage(string Url, string AltText, int SortOrder);
}
