namespace Messages.Contracts.Shopping;

/// <summary>
/// Integration message published by Shopping BC when a cart transitions to checkout.
/// Consumed by Orders BC to start the Checkout aggregate.
/// </summary>
public sealed record CheckoutInitiated(
    Guid CheckoutId,
    Guid CartId,
    Guid? CustomerId,
    IReadOnlyList<CheckoutLineItem> Items,
    DateTimeOffset InitiatedAt);
