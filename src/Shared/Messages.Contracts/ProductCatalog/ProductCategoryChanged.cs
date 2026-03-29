namespace Messages.Contracts.ProductCatalog;

/// <summary>
/// Integration message published when a product's category assignment changes.
/// Consumers like Listings BC use this to update category-based filtering and display.
/// </summary>
public sealed record ProductCategoryChanged(
    string Sku,
    string PreviousCategory,
    string NewCategory,
    DateTimeOffset OccurredAt);
