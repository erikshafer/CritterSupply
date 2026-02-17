namespace Shopping.Checkout;

public sealed record CheckoutStarted(
    Guid CheckoutId,
    Guid CartId,
    Guid? CustomerId,
    IReadOnlyList<CheckoutLineItem> Items,
    DateTimeOffset StartedAt);
