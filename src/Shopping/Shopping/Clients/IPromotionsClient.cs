namespace Shopping.Clients;

/// <summary>
/// Client interface for Promotions BC operations.
/// Used by Shopping BC to validate coupons and calculate discounts during cart operations.
/// </summary>
public interface IPromotionsClient
{
    /// <summary>
    /// Validates a coupon code for customer use.
    /// Returns validation result with promotion details if valid.
    /// </summary>
    Task<CouponValidationDto> ValidateCouponAsync(string couponCode, CancellationToken ct = default);

    /// <summary>
    /// Calculates discount for cart items with applied coupon codes.
    /// Returns discount breakdown per line item and totals.
    /// </summary>
    Task<DiscountCalculationDto> CalculateDiscountAsync(
        IReadOnlyList<CartItemDto> cartItems,
        IReadOnlyList<string> couponCodes,
        CancellationToken ct = default);
}

/// <summary>
/// Coupon validation result from Promotions BC.
/// Maps to CouponValidationResult from Promotions BC.
/// </summary>
public sealed record CouponValidationDto(
    bool IsValid,
    string CouponCode,
    Guid? PromotionId = null,
    string? PromotionName = null,
    string? DiscountType = null,
    decimal? DiscountValue = null,
    string? Reason = null);

/// <summary>
/// Discount calculation result from Promotions BC.
/// Maps to CalculateDiscountResponse from Promotions BC.
/// </summary>
public sealed record DiscountCalculationDto(
    IReadOnlyList<LineItemDiscountDto> LineItemDiscounts,
    decimal TotalDiscount,
    decimal OriginalTotal,
    decimal DiscountedTotal);

/// <summary>
/// Line item discount details from Promotions BC.
/// Maps to LineItemDiscount from Promotions BC.
/// </summary>
public sealed record LineItemDiscountDto(
    string Sku,
    decimal OriginalPrice,
    decimal DiscountedPrice,
    decimal DiscountAmount,
    string? AppliedCouponCode);

/// <summary>
/// Cart item for discount calculation request.
/// Maps to CartLineItem from Promotions BC.
/// </summary>
public sealed record CartItemDto(
    string Sku,
    int Quantity,
    decimal UnitPrice);
