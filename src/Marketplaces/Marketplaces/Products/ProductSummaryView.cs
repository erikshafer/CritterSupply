namespace Marketplaces.Products;

/// <summary>
/// Anti-corruption layer document representing product data from Product Catalog BC.
/// Maintained exclusively by integration event handlers — never by HTTP calls.
/// Keyed by SKU (string Id).
///
/// Carries only the fields that Marketplaces BC needs for listing submission:
/// ProductName, Category, BasePrice, and Status. This is intentionally a smaller
/// subset than the Listings BC's ProductSummaryView — each BC owns its own ACL.
/// </summary>
public sealed class ProductSummaryView
{
    /// <summary>
    /// Product SKU — serves as the Marten document Id.
    /// </summary>
    public string Id { get; set; } = null!;

    /// <summary>
    /// Human-readable product name for marketplace submissions.
    /// </summary>
    public string ProductName { get; set; } = null!;

    /// <summary>
    /// Internal product category aligned with Product Catalog BC taxonomy.
    /// Used for category mapping lookups during listing submission.
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Base price from Product Catalog. Used as the default listing price
    /// when the ListingApproved message does not carry a channel-specific price.
    /// </summary>
    public decimal? BasePrice { get; set; }

    /// <summary>
    /// Product lifecycle status as understood by Marketplaces BC.
    /// Maps from Product Catalog's ProductStatus values.
    /// </summary>
    public ProductSummaryStatus Status { get; set; }
}

/// <summary>
/// Product status as understood by the Marketplaces BC (anti-corruption layer).
/// Maps from Product Catalog's ProductStatus string values.
/// </summary>
public enum ProductSummaryStatus
{
    Active,
    ComingSoon,
    Discontinued,
    Deleted
}
