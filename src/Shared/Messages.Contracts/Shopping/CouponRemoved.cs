namespace Messages.Contracts.Shopping;

/// <summary>
/// Integration message: Coupon removed from shopping cart.
/// Published by Shopping BC for Customer Experience BC to trigger SSE push.
/// </summary>
public sealed record CouponRemoved(
    Guid CartId,
    Guid CustomerId,
    string CouponCode,
    DateTimeOffset RemovedAt);
