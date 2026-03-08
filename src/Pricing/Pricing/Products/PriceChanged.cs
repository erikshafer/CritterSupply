namespace Pricing.Products;

/// <summary>
/// Domain event: Price value changed (Published → Published).
/// Tracks old/new price for audit trail and Was/Now display.
/// ChangedBy required for audit compliance (FluentValidation enforces non-empty Guid).
/// </summary>
public sealed record PriceChanged(
    Guid ProductPriceId,
    string Sku,
    Money OldPrice,
    Money NewPrice,
    DateTimeOffset PreviousPriceSetAt,
    string? Reason,
    Guid ChangedBy,
    DateTimeOffset ChangedAt,
    Guid? BulkPricingJobId,
    Guid? SourceSuggestionId);
