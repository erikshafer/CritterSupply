using Storefront.Clients;
using Storefront.Composition;
using Wolverine.Http;

namespace Storefront.Api.Queries;

/// <summary>
/// Query to get composed product listing (Catalog BC + Inventory BC)
/// </summary>
public sealed record GetProductListing(string? Category, int Page, int PageSize);

public static class GetProductListingHandler
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;
    private const int MinPage = 1;

    [WolverineGet("/api/storefront/products")]
    public static async Task<ProductListingView> Handle(
        ICatalogClient catalogClient,
        string? category = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        // Apply pagination constraints
        var normalizedPage = page < MinPage ? MinPage : page;
        var normalizedPageSize = pageSize switch
        {
            <= 0 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => pageSize
        };

        // Query Catalog BC for product listing
        var pagedResult = await catalogClient.GetProductsAsync(category, normalizedPage, normalizedPageSize, ct);

        // Map to ProductCardView
        var productCards = pagedResult.Items.Select(p => new ProductCardView(
            p.Sku,
            p.Name,
            p.Price,
            p.Images.FirstOrDefault()?.Url ?? "",
            p.Category,
            IsInStock: true // TODO: Query Inventory BC for availability (Phase 2)
        )).ToList();

        return new ProductListingView(
            productCards,
            pagedResult.TotalCount,
            pagedResult.Page,
            pagedResult.PageSize);
    }
}
