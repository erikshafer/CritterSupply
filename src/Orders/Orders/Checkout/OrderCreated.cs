namespace Orders.Checkout;

public sealed record OrderCreated(
    Guid OrderId,
    DateTimeOffset CompletedAt);
