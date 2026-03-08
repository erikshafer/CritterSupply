namespace Pricing.Products;

/// <summary>
/// Domain event: Product registered in Pricing BC (triggered by ProductAdded from Catalog BC).
/// Creates ProductPrice event stream in Unpriced status.
/// This "registers" the SKU so subsequent SetPrice commands have a stream to append to.
/// Idempotency: If stream already exists, handler discards duplicate (at-least-once delivery guard).
/// </summary>
public sealed record ProductRegistered(
    Guid ProductPriceId,
    string Sku,
    DateTimeOffset RegisteredAt);
