namespace Promotions.Coupon;

/// <summary>
/// Strong-typed tag identifier for Coupon event streams in DCB queries.
/// Wraps the deterministic UUID v5 stream ID computed from a coupon code.
/// Required because Marten's RegisterTagType needs a value type with a single
/// public property and a constructor — raw Guid doesn't satisfy ValueTypeInfo.
/// </summary>
public sealed record CouponStreamTag(Guid Value)
{
    public static CouponStreamTag FromCode(string couponCode)
        => new(Coupon.StreamId(couponCode));
}
