namespace Promotions.Promotion;

/// <summary>
/// Command to record a redemption on the Promotion aggregate.
/// Triggered by OrderPlacedHandler after order placement.
/// Increments CurrentRedemptionCount and enforces UsageLimit.
/// Phase 1 (M30.0): Command defined but not yet invoked (Shopping BC integration pending).
/// Phase 2 (M30.1): OrderPlacedHandler will fan out to this command.
///
/// SUPERSEDED (M40.0): The DCB RedeemCouponHandler now emits CouponRedeemed,
/// and RecordPromotionRedemptionHandler reacts to that event via choreography.
/// This command is retained for backward compatibility and legacy test coverage.
/// New callers should use RedeemCoupon (with PromotionId) instead.
/// </summary>
public sealed record RecordPromotionRedemption(
    Guid PromotionId,
    Guid OrderId,
    Guid CustomerId,
    string CouponCode,
    DateTimeOffset RedeemedAt);
