using Storefront.Clients;
using Storefront.Composition;
using Wolverine.Http;

namespace Storefront.Queries;

/// <summary>
/// Query to get composed product listing (Catalog BC + Inventory BC)
/// </summary>
public sealed record GetProductListing(string? Category, int Page, int PageSize);

public static class GetProductListingHandler
{
    [WolverineGet("/api/storefront/products")]
    public static async Task<ProductListingView> Handle(
        ICatalogClient catalogClient,
        string? category = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        // Query Catalog BC for product listing
        var pagedResult = await catalogClient.GetProductsAsync(category, page, pageSize, ct);

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
