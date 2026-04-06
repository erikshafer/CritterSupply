namespace Messages.Contracts.Fulfillment;

/// <summary>
/// Integration message from Fulfillment to Orders.
/// Published when a shipment is backordered due to no stock availability.
/// Orders saga should notify the customer.
/// </summary>
public sealed record BackorderCreated(
    Guid OrderId,
    Guid ShipmentId,
    string Reason,
    DateTimeOffset CreatedAt);
