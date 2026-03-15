using Marten;
using Microsoft.AspNetCore.Http.HttpResults;
using Promotions.Coupon;
using Promotions.Discount;
using Wolverine.Http;

namespace Promotions.Api.Queries;

/// <summary>
/// HTTP POST endpoint to calculate discounts for a cart.
/// Used by Shopping BC to display discounted prices before checkout.
/// Phase 1: Single coupon, percentage discounts only, stub floor price (allow full discount).
/// Phase 2+: Pricing BC integration for real floor price enforcement.
/// </summary>
public sealed class CalculateDiscount
{
    [WolverinePost("/api/promotions/discounts/calculate")]
    public static async Task<Ok<CalculateDiscountResponse>> Handle(
        CalculateDiscountRequest request,
        IDocumentSession session,
        CancellationToken ct)
    {
        // Phase 1: No coupons applied → return zero discount
        if (request.CouponCodes.Count == 0)
        {
            return TypedResults.Ok(CalculateZeroDiscount(request.CartItems));
        }

        // Phase 1: Single coupon support only
        var couponCode = request.CouponCodes[0].ToUpperInvariant();

        // Look up coupon in projection
        var coupon = await session.LoadAsync<CouponLookupView>(couponCode, ct);

        // Invalid coupon → return zero discount (Shopping BC will show warning)
        if (coupon is null || coupon.Status != CouponStatus.Issued)
        {
            return TypedResults.Ok(CalculateZeroDiscount(request.CartItems));
        }

        // Load parent promotion to get discount details
        var promotion = await session.Events.AggregateStreamAsync<Promotions.Promotion.Promotion>(
            coupon.PromotionId,
            token: ct);

        // Invalid promotion → return zero discount
        if (promotion is null || promotion.Status != PromotionStatus.Active)
        {
            return TypedResults.Ok(CalculateZeroDiscount(request.CartItems));
        }

        // Check date range
        var now = DateTimeOffset.UtcNow;
        if (now < promotion.StartDate || now > promotion.EndDate)
        {
            return TypedResults.Ok(CalculateZeroDiscount(request.CartItems));
        }

        // Phase 1: Only PercentageOff discount type
        if (promotion.DiscountType != DiscountType.PercentageOff)
        {
            return TypedResults.Ok(CalculateZeroDiscount(request.CartItems));
        }

        // Calculate percentage discount for each line item
        var lineItemDiscounts = request.CartItems
            .Select(item => CalculatePercentageDiscount(item, promotion.DiscountValue, couponCode))
            .ToList();

        var originalTotal = request.CartItems.Sum(item => item.UnitPrice * item.Quantity);
        var totalDiscount = lineItemDiscounts.Sum(d => d.DiscountAmount);
        var discountedTotal = originalTotal - totalDiscount;

        return TypedResults.Ok(new CalculateDiscountResponse(
            LineItemDiscounts: lineItemDiscounts,
            TotalDiscount: totalDiscount,
            OriginalTotal: originalTotal,
            DiscountedTotal: discountedTotal));
    }

    /// <summary>
    /// Phase 1: Calculate percentage discount without floor price enforcement.
    /// Phase 2+: Query Pricing BC for floor price and clamp discount.
    /// </summary>
    private static LineItemDiscount CalculatePercentageDiscount(
        CartLineItem item,
        decimal discountPercentage,
        string couponCode)
    {
        var originalPrice = item.UnitPrice;
        var discountAmount = Math.Round(originalPrice * (discountPercentage / 100m), 2);
        var discountedPrice = originalPrice - discountAmount;

        // Phase 1: Stub floor price enforcement — allow full discount
        // Phase 2+: Query Pricing BC and clamp:
        //   var floorPrice = await pricingClient.GetFloorPrice(item.Sku);
        //   if (discountedPrice < floorPrice)
        //   {
        //       discountedPrice = floorPrice;
        //       discountAmount = originalPrice - floorPrice;
        //   }

        return new LineItemDiscount(
            Sku: item.Sku,
            OriginalPrice: originalPrice,
            DiscountedPrice: Math.Max(discountedPrice, 0), // Never negative
            DiscountAmount: discountAmount * item.Quantity,
            AppliedCouponCode: couponCode);
    }

    /// <summary>
    /// Returns zero discount response when no valid coupon is applied.
    /// </summary>
    private static CalculateDiscountResponse CalculateZeroDiscount(IReadOnlyList<CartLineItem> cartItems)
    {
        var originalTotal = cartItems.Sum(item => item.UnitPrice * item.Quantity);

        return new CalculateDiscountResponse(
            LineItemDiscounts: Array.Empty<LineItemDiscount>(),
            TotalDiscount: 0m,
            OriginalTotal: originalTotal,
            DiscountedTotal: originalTotal);
    }
}
