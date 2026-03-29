namespace Messages.Contracts.ProductCatalog;

/// <summary>
/// Integration message published when a product's physical dimensions or weight change.
/// Consumers like Listings BC use this to update shipping-related listing attributes.
/// </summary>
public sealed record ProductDimensionsChanged(
    string Sku,
    decimal Weight,
    decimal Length,
    decimal Width,
    decimal Height,
    DateTimeOffset OccurredAt);
