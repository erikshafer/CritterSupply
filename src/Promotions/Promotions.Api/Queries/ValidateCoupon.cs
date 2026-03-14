using Marten;
using Microsoft.AspNetCore.Http.HttpResults;
using Promotions.Coupon;
using Wolverine.Http;

namespace Promotions.Api.Queries;

/// <summary>
/// HTTP GET endpoint to validate a coupon code.
/// Used by Shopping BC during checkout to verify coupon eligibility.
/// Phase 1: Simple validation (exists, not redeemed, promotion active, not expired).
/// Phase 2+: Add usage limit checks, customer eligibility, minimum order checks.
/// </summary>
public sealed class ValidateCoupon
{
    [WolverineGet("/api/promotions/coupons/{code}/validate")]
    public static async Task<Ok<CouponValidationResult>> Handle(
        string code,
        IDocumentSession session,
        CancellationToken ct)
    {
        // Normalize code to uppercase for case-insensitive lookup
        var normalizedCode = code.ToUpperInvariant();

        // Look up coupon in projection (fast, no event replay)
        var coupon = await session.LoadAsync<CouponLookupView>(normalizedCode, ct);

        if (coupon is null)
        {
            return TypedResults.Ok(CouponValidationResult.Invalid("Coupon not found", normalizedCode));
        }

        // Business rule: coupon must be in Issued status
        if (coupon.Status != CouponStatus.Issued)
        {
            return TypedResults.Ok(CouponValidationResult.Invalid(
                $"Coupon is {coupon.Status.ToString().ToLowerInvariant()}",
                normalizedCode));
        }

        // Load parent promotion to check if active and not expired
        var promotion = await session.Events.AggregateStreamAsync<Promotions.Promotion.Promotion>(
            coupon.PromotionId,
            token: ct);

        if (promotion is null)
        {
            return TypedResults.Ok(CouponValidationResult.Invalid(
                "Parent promotion not found",
                normalizedCode));
        }

        // Business rule: promotion must be Active
        if (promotion.Status != PromotionStatus.Active)
        {
            return TypedResults.Ok(CouponValidationResult.Invalid(
                $"Promotion is {promotion.Status.ToString().ToLowerInvariant()}",
                normalizedCode));
        }

        // Business rule: promotion must not be expired (check end date)
        var now = DateTimeOffset.UtcNow;
        if (now > promotion.EndDate)
        {
            return TypedResults.Ok(CouponValidationResult.Invalid(
                "Promotion has expired",
                normalizedCode));
        }

        // Business rule: promotion must have started (check start date)
        if (now < promotion.StartDate)
        {
            return TypedResults.Ok(CouponValidationResult.Invalid(
                "Promotion has not started yet",
                normalizedCode));
        }

        // All checks passed — coupon is valid
        return TypedResults.Ok(CouponValidationResult.Valid(
            code: normalizedCode,
            promotionId: promotion.Id,
            promotionName: promotion.Name,
            discountType: promotion.DiscountType,
            discountValue: promotion.DiscountValue));
    }
}
