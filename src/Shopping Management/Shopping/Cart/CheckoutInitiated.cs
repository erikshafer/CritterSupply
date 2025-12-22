namespace Shopping.Cart;

public sealed record CheckoutInitiated(
    Guid CartId,
    Guid CheckoutId,
    Guid? CustomerId,
    IReadOnlyList<CartLineItem> Items,
    DateTimeOffset InitiatedAt);
