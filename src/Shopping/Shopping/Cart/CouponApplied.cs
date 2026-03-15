namespace Shopping.Cart;

public sealed record CouponApplied(
    Guid CartId,
    string CouponCode,
    decimal DiscountAmount,
    DateTimeOffset AppliedAt);
