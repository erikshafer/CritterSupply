namespace Pricing.Products;

/// <summary>
/// Domain event: Floor price set or changed.
/// Floor price = internal margin protection (minimum allowed base price).
/// Phase 1: Single floor price. Phase 2+: separate MapPrice for vendor MAP obligations.
/// ExpiresAt: nullable — policy bounds may have time limits (e.g., "until end of Q3").
/// </summary>
public sealed record FloorPriceSet(
    Guid ProductPriceId,
    string Sku,
    Money? OldFloorPrice,
    Money FloorPrice,
    Guid SetBy,
    DateTimeOffset SetAt,
    DateTimeOffset? ExpiresAt);
