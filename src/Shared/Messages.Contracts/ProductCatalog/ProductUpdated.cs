namespace Messages.Contracts.ProductCatalog;

/// <summary>
/// Integration message published when product details are updated.
/// Other BCs may react to this (e.g., Customer Experience invalidates cached listings).
/// </summary>
[Obsolete("Use granular product events instead (ProductContentUpdated, ProductCategoryChanged, etc.). This contract will be removed in a future milestone.")]
public sealed record ProductUpdated(
    string Sku,
    string Name,
    string Category,
    DateTimeOffset UpdatedAt);
