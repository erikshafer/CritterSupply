namespace Messages.Contracts.Pricing;

/// <summary>
/// Integration message published by Pricing BC when an existing product's price changes.
/// Consumed by Shopping BC (may trigger cart price refresh notifications),
/// Customer Experience BFF (updates product displays), and analytics BCs.
/// </summary>
public sealed record PriceUpdated(
    string Sku,
    decimal OldPrice,
    decimal NewPrice,
    string Currency,
    DateTimeOffset EffectiveAt);
