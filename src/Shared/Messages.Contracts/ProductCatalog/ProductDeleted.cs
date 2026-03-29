namespace Messages.Contracts.ProductCatalog;

/// <summary>
/// Integration message published when a product is soft-deleted from the catalog.
/// Consumers like Listings BC use this to end any active listings for the product.
/// </summary>
public sealed record ProductDeleted(
    string Sku,
    DateTimeOffset OccurredAt);
