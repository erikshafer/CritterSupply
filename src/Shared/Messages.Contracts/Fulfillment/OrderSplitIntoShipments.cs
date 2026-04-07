namespace Messages.Contracts.Fulfillment;

// Orders saga handler pending — see M41.0 S4 coordinated update

/// <summary>
/// Integration event published when an order is split into multiple shipments
/// because items cannot be fulfilled from a single FC.
/// </summary>
public sealed record OrderSplitIntoShipments(
    Guid OrderId,
    int ShipmentCount,
    DateTimeOffset SplitAt);
