namespace Messages.Contracts.ProductCatalog;

/// <summary>
/// Integration message published when a product is discontinued.
/// Other BCs may react to this (e.g., Orders may prevent new purchases).
/// </summary>
public sealed record ProductDiscontinued(
    string Sku,
    DateTimeOffset DiscontinuedAt);
