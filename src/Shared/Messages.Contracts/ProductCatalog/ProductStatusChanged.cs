namespace Messages.Contracts.ProductCatalog;

/// <summary>
/// Integration message published when a product's lifecycle status changes
/// (e.g., Draft → Active, Active → Discontinued).
/// Consumers like Listings BC use this to enforce listing eligibility rules.
/// </summary>
public sealed record ProductStatusChanged(
    string Sku,
    string PreviousStatus,
    string NewStatus,
    DateTimeOffset OccurredAt);
