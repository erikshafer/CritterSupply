namespace Promotions.Discount;

/// <summary>
/// Response containing calculated discounts for cart.
/// Phase 1: Simple percentage discount, no floor price enforcement.
/// Phase 2+: Floor price clamping details, multiple coupon handling.
/// </summary>
public sealed record CalculateDiscountResponse(
    IReadOnlyList<LineItemDiscount> LineItemDiscounts,
    decimal TotalDiscount,
    decimal OriginalTotal,
    decimal DiscountedTotal);
