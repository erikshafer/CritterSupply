namespace Pricing.Products;

/// <summary>
/// Domain event: First price set for a product.
/// Transitions ProductPrice from Unpriced → Published.
/// Distinct from PriceChanged per UX Engineer recommendation (marks pricing lifecycle start).
/// </summary>
public sealed record InitialPriceSet(
    Guid ProductPriceId,
    string Sku,
    Money Price,
    Money? FloorPrice,
    Money? CeilingPrice,
    Guid SetBy,
    DateTimeOffset PricedAt);
