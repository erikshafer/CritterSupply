namespace Storefront.Composition;

/// <summary>
/// Composed view for product listing page (aggregates Catalog BC + Inventory BC)
/// </summary>
public sealed record ProductListingView(
    IReadOnlyList<ProductCardView> Products,
    int TotalCount,
    int Page,
    int PageSize);

/// <summary>
/// Product card for listing page
/// </summary>
public sealed record ProductCardView(
    string Sku,
    string Name,
    decimal Price,
    string PrimaryImageUrl,
    string Category,
    bool IsInStock);  // From Inventory BC (future)
