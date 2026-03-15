namespace Messages.Contracts.Shopping;

/// <summary>
/// Integration message: Coupon applied to shopping cart.
/// Published by Shopping BC for Customer Experience BC to trigger SSE push.
/// </summary>
public sealed record CouponApplied(
    Guid CartId,
    Guid CustomerId,
    string CouponCode,
    decimal DiscountAmount,
    DateTimeOffset AppliedAt);
