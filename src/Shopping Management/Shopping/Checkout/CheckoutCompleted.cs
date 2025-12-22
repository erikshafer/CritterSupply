namespace Shopping.Checkout;

public sealed record CheckoutCompleted(
    Guid OrderId,
    DateTimeOffset CompletedAt);
