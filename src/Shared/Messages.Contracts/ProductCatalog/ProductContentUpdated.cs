namespace Messages.Contracts.ProductCatalog;

/// <summary>
/// Integration message published when a product's content (name or description) is updated.
/// Consumers like Listings BC use this to keep their product summary views current.
/// </summary>
public sealed record ProductContentUpdated(
    string Sku,
    string Name,
    string Description,
    DateTimeOffset OccurredAt);
