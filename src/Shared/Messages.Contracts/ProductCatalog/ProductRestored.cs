namespace Messages.Contracts.ProductCatalog;

/// <summary>
/// Integration message published when a previously soft-deleted product is restored.
/// Consumers like Listings BC may use this to allow new listings for the product.
/// </summary>
public sealed record ProductRestored(
    string Sku,
    DateTimeOffset OccurredAt);
