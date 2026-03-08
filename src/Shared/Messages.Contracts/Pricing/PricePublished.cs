namespace Messages.Contracts.Pricing;

/// <summary>
/// Integration message published by Pricing BC when a product receives its first price.
/// Consumed by Shopping BC (enables AddItemToCart), Customer Experience BFF (displays price),
/// and other BCs that need to know when pricing becomes available.
/// </summary>
public sealed record PricePublished(
    string Sku,
    decimal BasePrice,
    string Currency,
    DateTimeOffset PublishedAt);
