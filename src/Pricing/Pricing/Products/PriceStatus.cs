namespace Pricing.Products;

/// <summary>
/// Represents the lifecycle state of a ProductPrice aggregate.
/// Phase 1: Simple three-state lifecycle. Phase 2+: May add Draft for campaign staging.
/// </summary>
public enum PriceStatus
{
    /// <summary>
    /// Product registered but no price set yet. Cannot add to cart.
    /// Transitions to: Published (via InitialPriceSet)
    /// </summary>
    Unpriced,

    /// <summary>
    /// Product has an active price and can be added to cart.
    /// Transitions to: Discontinued (via PriceDiscontinued)
    /// </summary>
    Published,

    /// <summary>
    /// Product discontinued. Terminal state. Cannot change price.
    /// No transitions — terminal.
    /// </summary>
    Discontinued
}
