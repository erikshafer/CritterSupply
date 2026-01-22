namespace ProductCatalog.Products;

/// <summary>
/// Product lifecycle status.
/// </summary>
public enum ProductStatus
{
    /// <summary>
    /// Currently available for sale.
    /// </summary>
    Active,

    /// <summary>
    /// No longer sold (but still in system for historical orders).
    /// </summary>
    Discontinued,

    /// <summary>
    /// Announced but not yet available for purchase.
    /// </summary>
    ComingSoon,

    /// <summary>
    /// Seasonal items (e.g., holiday-themed products).
    /// </summary>
    OutOfSeason
}
