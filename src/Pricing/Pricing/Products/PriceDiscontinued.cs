namespace Pricing.Products;

/// <summary>
/// Domain event: Product discontinued (triggered by ProductDiscontinued integration event from Catalog BC).
/// Transitions ProductPrice to Discontinued (terminal state).
/// Clears any pending scheduled changes.
/// </summary>
public sealed record PriceDiscontinued(
    Guid ProductPriceId,
    string Sku,
    DateTimeOffset DiscontinuedAt);
