namespace Messages.Contracts.ProductCatalog;

/// <summary>
/// Integration message published when a new product is added to the catalog.
/// Other BCs may react to this (e.g., Inventory creates stock record).
/// Enriched in M36.1 with optional Status, Brand, and HasDimensions fields.
/// Nullable fields preserve backward compatibility with existing consumers.
/// </summary>
public sealed record ProductAdded(
    string Sku,
    string Name,
    string Category,
    DateTimeOffset AddedAt,
    string? Status = null,
    string? Brand = null,
    bool? HasDimensions = null);
