namespace Promotions.Discount;

/// <summary>
/// Request to calculate discounts for a cart.
/// Used by Shopping BC to get discount amounts before checkout.
/// Phase 1: Single coupon support, percentage discounts only.
/// Phase 2+: Multiple coupons, stacking rules, fixed amount, free shipping.
/// </summary>
public sealed record CalculateDiscountRequest(
    IReadOnlyList<CartLineItem> CartItems,
    IReadOnlyList<string> CouponCodes);
