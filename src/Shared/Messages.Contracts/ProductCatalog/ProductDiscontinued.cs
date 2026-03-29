namespace Messages.Contracts.ProductCatalog;

/// <summary>
/// Integration message published when a product is discontinued.
/// Other BCs may react to this (e.g., Orders may prevent new purchases).
/// Enriched in M36.1 with Reason and IsRecall fields for recall cascade support.
/// IsRecall defaults to false so existing callers remain valid.
/// </summary>
public sealed record ProductDiscontinued(
    string Sku,
    DateTimeOffset DiscontinuedAt,
    string? Reason = null,
    bool IsRecall = false);
