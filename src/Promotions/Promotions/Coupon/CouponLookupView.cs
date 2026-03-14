namespace Promotions.Coupon;

/// <summary>
/// Read model for quick coupon lookups without event replay.
/// Projection: Inline (zero lag, same transaction as command).
/// Document ID: Coupon code (uppercase).
/// Phase 1: Simple lookup for validation endpoint.
/// Phase 2+: Add redemption counter for usage limits.
/// </summary>
public sealed record CouponLookupView
{
    /// <summary>
    /// Document ID — coupon code (uppercase).
    /// </summary>
    public string Id { get; init; } = null!;

    /// <summary>
    /// Coupon code (normalized to uppercase).
    /// </summary>
    public string Code { get; init; } = null!;

    /// <summary>
    /// Parent promotion ID.
    /// </summary>
    public Guid PromotionId { get; init; }

    /// <summary>
    /// Coupon lifecycle state.
    /// </summary>
    public CouponStatus Status { get; init; }

    /// <summary>
    /// When the coupon was issued.
    /// </summary>
    public DateTimeOffset IssuedAt { get; init; }

    /// <summary>
    /// When the coupon was redeemed (null if not yet redeemed).
    /// </summary>
    public DateTimeOffset? RedeemedAt { get; init; }

    /// <summary>
    /// Order ID where the coupon was redeemed (null if not yet redeemed).
    /// </summary>
    public Guid? OrderId { get; init; }
}
