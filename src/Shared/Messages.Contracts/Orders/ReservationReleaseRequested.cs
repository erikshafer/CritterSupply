namespace Messages.Contracts.Orders;

/// <summary>
/// Integration message published by Orders BC when it wants Inventory to release a reservation.
/// Sent during compensation flows (e.g., payment failed, order cancelled).
/// Consumed by Inventory BC.
/// </summary>
public sealed record ReservationReleaseRequested(
    Guid OrderId,
    Guid ReservationId,
    string Reason,
    DateTimeOffset RequestedAt);
