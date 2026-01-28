namespace Messages.Contracts.ProductCatalog;

/// <summary>
/// Integration message published when product details are updated.
/// Other BCs may react to this (e.g., Customer Experience invalidates cached listings).
/// </summary>
public sealed record ProductUpdated(
    string Sku,
    string Name,
    string Category,
    DateTimeOffset UpdatedAt);
