namespace Pricing.Products;

/// <summary>
/// Command: Change the base price for a Published product.
/// Requires product to be in Published status (cannot change Unpriced or Discontinued products).
/// Tracks previous price and timestamp for Was/Now display.
/// </summary>
public sealed record ChangePrice(
    string Sku,
    decimal NewAmount,
    string Currency,
    string? Reason,
    Guid ChangedBy,
    DateTimeOffset ChangedAt,
    Guid? BulkPricingJobId = null,
    Guid? SourceSuggestionId = null);
