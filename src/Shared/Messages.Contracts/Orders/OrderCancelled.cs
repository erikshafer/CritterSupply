namespace Messages.Contracts.Orders;

/// <summary>
/// Integration event published by Orders BC when an order is cancelled.
/// Triggers compensation flows across bounded contexts (inventory release, payment refund).
/// Consumed by Inventory, Fulfillment, and Customer Experience BCs.
/// </summary>
public sealed record OrderCancelled(
    Guid OrderId,
    Guid CustomerId,
    string Reason,
    DateTimeOffset CancelledAt);
