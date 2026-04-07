namespace Messages.Contracts.Fulfillment;

// Orders saga handler pending — see M41.0 S4 coordinated update

/// <summary>
/// Integration event published when fulfillment is cancelled before carrier handoff.
/// Published to Orders BC.
/// </summary>
public sealed record FulfillmentCancelled(
    Guid OrderId,
    Guid ShipmentId,
    string Reason,
    DateTimeOffset CancelledAt);
