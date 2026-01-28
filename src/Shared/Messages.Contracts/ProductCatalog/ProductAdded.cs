namespace Messages.Contracts.ProductCatalog;

/// <summary>
/// Integration message published when a new product is added to the catalog.
/// Other BCs may react to this (e.g., Inventory creates stock record).
/// </summary>
public sealed record ProductAdded(
    string Sku,
    string Name,
    string Category,
    DateTimeOffset AddedAt);
