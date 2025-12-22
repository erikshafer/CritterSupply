using Shopping.Cart;

namespace Shopping.Checkout;

public sealed record CheckoutStarted(
    Guid CheckoutId,
    Guid CartId,
    Guid? CustomerId,
    IReadOnlyList<CartLineItem> Items,
    DateTimeOffset StartedAt);
