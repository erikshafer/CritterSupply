namespace Promotions.Promotion;

/// <summary>
/// Command to record a redemption on the Promotion aggregate.
/// Triggered by OrderPlacedHandler after order placement.
/// Increments CurrentRedemptionCount and enforces UsageLimit.
/// Phase 1 (M30.0): Command defined but not yet invoked (Shopping BC integration pending).
/// Phase 2 (M30.1): OrderPlacedHandler will fan out to this command.
/// </summary>
public sealed record RecordPromotionRedemption(
    Guid PromotionId,
    Guid OrderId,
    Guid CustomerId,
    string CouponCode,
    DateTimeOffset RedeemedAt);
