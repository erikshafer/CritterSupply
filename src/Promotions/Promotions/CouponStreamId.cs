namespace Promotions;

/// <summary>
/// Strong-typed tag identifier for Coupon event streams.
/// Required by Marten's DCB tag registration (Guid has 2 public properties in .NET 10
/// and cannot be used directly as a tag type.)
/// Registered via: opts.Events.RegisterTagType&lt;CouponStreamId&gt;("coupon").ForAggregate&lt;Coupon.Coupon&gt;()
/// </summary>
public sealed record CouponStreamId(Guid Value);
