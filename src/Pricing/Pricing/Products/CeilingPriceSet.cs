namespace Pricing.Products;

/// <summary>
/// Domain event: Ceiling price (MAP constraint) set or changed.
/// Ceiling price = maximum allowed base price (MAP constraint or policy ceiling).
/// ExpiresAt: nullable — policy bounds may have time limits.
/// </summary>
public sealed record CeilingPriceSet(
    Guid ProductPriceId,
    string Sku,
    Money? OldCeilingPrice,
    Money CeilingPrice,
    Guid SetBy,
    DateTimeOffset SetAt,
    DateTimeOffset? ExpiresAt);
