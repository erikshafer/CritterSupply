namespace Listings.ProductSummary;

/// <summary>
/// Anti-corruption layer document representing product data from Product Catalog BC.
/// Maintained exclusively by integration event handlers — never by HTTP calls.
/// Keyed by SKU (string Id).
/// </summary>
public sealed record ProductSummaryView
{
    /// <summary>
    /// Product SKU — serves as the Marten document Id.
    /// </summary>
    public string Id { get; init; } = null!;

    public string Name { get; init; } = null!;

    public string? Description { get; init; }

    public string? Category { get; init; }

    /// <summary>
    /// Product lifecycle status from Product Catalog BC.
    /// </summary>
    public ProductSummaryStatus Status { get; init; }

    public string? Brand { get; init; }

    public bool HasDimensions { get; init; }

    public IReadOnlyList<string> ImageUrls { get; init; } = [];
}

/// <summary>
/// Product status as understood by the Listings BC (anti-corruption layer).
/// Maps from Product Catalog's ProductStatus values.
/// </summary>
public enum ProductSummaryStatus
{
    Active,
    ComingSoon,
    Discontinued,
    Deleted
}
