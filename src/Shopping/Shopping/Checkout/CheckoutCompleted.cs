namespace Shopping.Checkout;

public sealed record CheckoutCompleted(
    Guid CheckoutId,
    Guid OrderId,
    DateTimeOffset CompletedAt);
