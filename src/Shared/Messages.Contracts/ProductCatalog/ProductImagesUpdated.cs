namespace Messages.Contracts.ProductCatalog;

/// <summary>
/// Integration message published when a product's images are updated.
/// Consumers like Listings BC use this to refresh product imagery in listings.
/// </summary>
public sealed record ProductImagesUpdated(
    string Sku,
    IReadOnlyList<string> ImageUrls,
    DateTimeOffset OccurredAt);
