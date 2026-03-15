namespace Promotions.Promotion;

/// <summary>
/// Domain event: A batch of coupons was generated for this promotion.
/// The individual coupons are created as separate Coupon aggregates via fan-out.
/// </summary>
public sealed record CouponBatchGenerated(
    Guid PromotionId,
    Guid BatchId,
    string Prefix,
    int Count,
    DateTimeOffset GeneratedAt);
