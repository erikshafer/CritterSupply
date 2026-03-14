namespace Promotions.Coupon;

/// <summary>
/// Response model for coupon validation endpoint.
/// Phase 1: Simple valid/invalid with parent promotion data.
/// Phase 2+: Add discount amount calculation.
/// </summary>
public sealed record CouponValidationResult
{
    /// <summary>
    /// Whether the coupon is valid for use.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Validation failure reason (null if valid).
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// Coupon code (uppercase).
    /// </summary>
    public string? CouponCode { get; init; }

    /// <summary>
    /// Parent promotion ID (null if coupon not found).
    /// </summary>
    public Guid? PromotionId { get; init; }

    /// <summary>
    /// Parent promotion name (null if coupon not found).
    /// </summary>
    public string? PromotionName { get; init; }

    /// <summary>
    /// Discount type (null if coupon not found).
    /// </summary>
    public DiscountType? DiscountType { get; init; }

    /// <summary>
    /// Discount value (null if coupon not found).
    /// Phase 1: For PercentageOff, this is 0-100 (e.g., 15 = 15% off).
    /// Phase 2+: For FixedAmountOff, this is USD amount.
    /// </summary>
    public decimal? DiscountValue { get; init; }

    /// <summary>
    /// Factory: Valid coupon.
    /// </summary>
    public static CouponValidationResult Valid(
        string code,
        Guid promotionId,
        string promotionName,
        DiscountType discountType,
        decimal discountValue)
    {
        return new CouponValidationResult
        {
            IsValid = true,
            CouponCode = code,
            PromotionId = promotionId,
            PromotionName = promotionName,
            DiscountType = discountType,
            DiscountValue = discountValue
        };
    }

    /// <summary>
    /// Factory: Invalid coupon.
    /// </summary>
    public static CouponValidationResult Invalid(string reason, string? code = null)
    {
        return new CouponValidationResult
        {
            IsValid = false,
            Reason = reason,
            CouponCode = code
        };
    }
}
