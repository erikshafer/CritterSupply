namespace Pricing.Products;

/// <summary>
/// Read model for current product pricing - the hot path query.
/// Marten document key: SKU string (not Guid) for direct lookup.
/// ProjectionLifecycle: Inline (zero lag, same transaction as command).
///
/// CRITICAL: Marten string IDs are case-sensitive. All SKUs must be normalized
/// to ToUpperInvariant() at API boundary before querying.
/// </summary>
public sealed record CurrentPriceView
{
    /// <summary>
    /// Marten document ID = SKU string (normalized to uppercase).
    /// Enables: session.LoadAsync&lt;CurrentPriceView&gt;("DOG-FOOD-5LB")
    /// </summary>
    public string Id { get; init; } = null!;

    /// <summary>
    /// Canonical SKU string (normalized to uppercase).
    /// </summary>
    public string Sku { get; init; } = null!;

    /// <summary>
    /// Current base price amount (denormalized from Money value object).
    /// </summary>
    public decimal BasePrice { get; init; }

    /// <summary>
    /// Currency code (hardcoded "USD" for Phase 1).
    /// </summary>
    public string Currency { get; init; } = "USD";

    /// <summary>
    /// Minimum allowed base price (margin protection).
    /// Null if no floor price set.
    /// </summary>
    public decimal? FloorPrice { get; init; }

    /// <summary>
    /// Maximum allowed base price (MAP constraint).
    /// Null if no ceiling price set.
    /// </summary>
    public decimal? CeilingPrice { get; init; }

    /// <summary>
    /// Previous base price (for Was/Now display in BFF).
    /// Null if no previous price.
    /// </summary>
    public decimal? PreviousBasePrice { get; init; }

    /// <summary>
    /// When the previous price was current (UTC).
    /// Used for Was/Now TTL calculation (30 days from price drop).
    /// </summary>
    public DateTimeOffset? PreviousPriceSetAt { get; init; }

    /// <summary>
    /// Lifecycle state: Unpriced | Published | Discontinued.
    /// </summary>
    public PriceStatus Status { get; init; }

    /// <summary>
    /// Whether a scheduled price change is pending.
    /// True if PendingSchedule exists in aggregate.
    /// </summary>
    public bool HasPendingSchedule { get; init; }

    /// <summary>
    /// When the pending scheduled change will activate (UTC).
    /// Null if no pending schedule.
    /// </summary>
    public DateTimeOffset? ScheduledChangeAt { get; init; }

    /// <summary>
    /// Scheduled price amount.
    /// Null if no pending schedule.
    /// </summary>
    public decimal? ScheduledPrice { get; init; }

    /// <summary>
    /// When this view was last updated (UTC).
    /// </summary>
    public DateTimeOffset LastUpdatedAt { get; init; }
}
