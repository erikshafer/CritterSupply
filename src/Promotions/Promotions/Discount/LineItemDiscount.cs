namespace Promotions.Discount;

/// <summary>
/// Represents the discount applied to a specific line item.
/// Phase 1: Simple percentage discount, no floor price enforcement.
/// Phase 2+: Floor price clamping, show original vs clamped discount.
/// </summary>
public sealed record LineItemDiscount(
    string Sku,
    decimal OriginalPrice,
    decimal DiscountedPrice,
    decimal DiscountAmount,
    string AppliedCouponCode);
