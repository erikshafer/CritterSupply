using Shopping.Clients;

namespace Shopping.Api.IntegrationTests.Stubs;

/// <summary>
/// Stub implementation of IPromotionsClient for testing.
/// Returns predefined coupon validation and discount calculation results
/// without making real HTTP calls to Promotions BC.
/// </summary>
public sealed class StubPromotionsClient : IPromotionsClient
{
    private readonly Dictionary<string, CouponConfig> _coupons = new();

    /// <summary>
    /// Configure a valid coupon with its promotion details.
    /// </summary>
    public void SetValidCoupon(string couponCode, decimal discountPercentage, string promotionName = "Test Promotion")
    {
        var normalizedCode = couponCode.ToUpperInvariant();
        _coupons[normalizedCode] = new CouponConfig(
            IsValid: true,
            PromotionId: Guid.CreateVersion7(),
            PromotionName: promotionName,
            DiscountPercentage: discountPercentage);
    }

    /// <summary>
    /// Configure an invalid coupon with a reason.
    /// </summary>
    public void SetInvalidCoupon(string couponCode, string reason)
    {
        var normalizedCode = couponCode.ToUpperInvariant();
        _coupons[normalizedCode] = new CouponConfig(
            IsValid: false,
            PromotionId: null,
            PromotionName: null,
            DiscountPercentage: 0,
            Reason: reason);
    }

    /// <summary>
    /// Remove a coupon configuration (simulates coupon not found).
    /// </summary>
    public void RemoveCoupon(string couponCode)
    {
        var normalizedCode = couponCode.ToUpperInvariant();
        _coupons.Remove(normalizedCode);
    }

    /// <summary>
    /// Clear all configured coupons.
    /// </summary>
    public void Clear()
    {
        _coupons.Clear();
    }

    public Task<CouponValidationDto> ValidateCouponAsync(string couponCode, CancellationToken ct = default)
    {
        var normalizedCode = couponCode.ToUpperInvariant();

        if (_coupons.TryGetValue(normalizedCode, out var config))
        {
            return Task.FromResult(new CouponValidationDto(
                IsValid: config.IsValid,
                CouponCode: normalizedCode,
                PromotionId: config.PromotionId,
                PromotionName: config.PromotionName,
                DiscountType: config.IsValid ? "PercentageOff" : null,
                DiscountValue: config.IsValid ? config.DiscountPercentage : null,
                Reason: config.Reason));
        }

        // Coupon not found
        return Task.FromResult(new CouponValidationDto(
            IsValid: false,
            CouponCode: normalizedCode,
            Reason: "Coupon not found"));
    }

    public Task<DiscountCalculationDto> CalculateDiscountAsync(
        IReadOnlyList<CartItemDto> cartItems,
        IReadOnlyList<string> couponCodes,
        CancellationToken ct = default)
    {
        if (couponCodes.Count == 0)
        {
            // No coupons - return zero discount
            var originalTotal = cartItems.Sum(i => i.UnitPrice * i.Quantity);
            return Task.FromResult(new DiscountCalculationDto(
                LineItemDiscounts: cartItems.Select(i => new LineItemDiscountDto(
                    i.Sku,
                    i.UnitPrice,
                    i.UnitPrice,
                    0m,
                    null)).ToList(),
                TotalDiscount: 0m,
                OriginalTotal: originalTotal,
                DiscountedTotal: originalTotal));
        }

        var couponCode = couponCodes[0];
        var normalizedCode = couponCode.ToUpperInvariant();

        if (!_coupons.TryGetValue(normalizedCode, out var config) || !config.IsValid)
        {
            // Invalid coupon - return zero discount
            var originalTotal = cartItems.Sum(i => i.UnitPrice * i.Quantity);
            return Task.FromResult(new DiscountCalculationDto(
                LineItemDiscounts: cartItems.Select(i => new LineItemDiscountDto(
                    i.Sku,
                    i.UnitPrice,
                    i.UnitPrice,
                    0m,
                    null)).ToList(),
                TotalDiscount: 0m,
                OriginalTotal: originalTotal,
                DiscountedTotal: originalTotal));
        }

        // Calculate percentage discount per line item
        var discountMultiplier = config.DiscountPercentage / 100m;
        var lineItemDiscounts = cartItems.Select(item =>
        {
            var lineTotal = item.UnitPrice * item.Quantity;
            var discountAmount = Math.Round(lineTotal * discountMultiplier, 2);
            var discountedPrice = item.UnitPrice - Math.Round(item.UnitPrice * discountMultiplier, 2);

            return new LineItemDiscountDto(
                item.Sku,
                item.UnitPrice,
                Math.Max(0, discountedPrice),
                discountAmount,
                normalizedCode);
        }).ToList();

        var totalOriginal = cartItems.Sum(i => i.UnitPrice * i.Quantity);
        var totalDiscount = lineItemDiscounts.Sum(d => d.DiscountAmount);

        return Task.FromResult(new DiscountCalculationDto(
            LineItemDiscounts: lineItemDiscounts,
            TotalDiscount: totalDiscount,
            OriginalTotal: totalOriginal,
            DiscountedTotal: totalOriginal - totalDiscount));
    }

    private record CouponConfig(
        bool IsValid,
        Guid? PromotionId,
        string? PromotionName,
        decimal DiscountPercentage,
        string? Reason = null);
}
