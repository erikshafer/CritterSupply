namespace Shopping.Cart;

public sealed record CouponRemoved(
    Guid CartId,
    string CouponCode,
    DateTimeOffset RemovedAt);
