namespace Promotions.Promotion;

/// <summary>
/// Command to generate a batch of coupons for a promotion.
/// Uses fan-out pattern: one command creates N coupons.
/// Phase 1: Simple sequential code generation.
/// </summary>
public sealed record GenerateCouponBatch(
    Guid PromotionId,
    string Prefix,
    int Count);
