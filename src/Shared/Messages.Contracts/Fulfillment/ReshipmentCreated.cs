namespace Messages.Contracts.Fulfillment;

// Orders saga handler pending — see M41.0 S4 coordinated update

/// <summary>
/// Integration event published when a reshipment is created for a lost, returned,
/// or disputed shipment. Published to Orders BC.
/// </summary>
public sealed record ReshipmentCreated(
    Guid OrderId,
    Guid OriginalShipmentId,
    Guid NewShipmentId,
    string Reason,
    DateTimeOffset CreatedAt);
