namespace Pricing.Products;

/// <summary>
/// Command: Set initial base price for an Unpriced product.
/// Transitions product from Unpriced → Published status.
/// Can optionally set floor/ceiling constraints.
/// </summary>
public sealed record SetInitialPrice(
    string Sku,
    decimal Amount,
    string Currency,
    decimal? FloorAmount,
    decimal? CeilingAmount,
    Guid SetBy,
    DateTimeOffset PricedAt);
