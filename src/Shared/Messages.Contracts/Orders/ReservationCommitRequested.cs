namespace Messages.Contracts.Orders;

/// <summary>
/// Integration message published by Orders BC when it wants Inventory to commit a reservation.
/// Sent after payment is confirmed and order is ready to proceed to fulfillment.
/// Consumed by Inventory BC.
/// </summary>
public sealed record ReservationCommitRequested(
    Guid OrderId,
    Guid ReservationId,
    DateTimeOffset RequestedAt);
