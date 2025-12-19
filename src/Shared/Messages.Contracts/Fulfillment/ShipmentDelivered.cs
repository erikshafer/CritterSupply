namespace Messages.Contracts.Fulfillment;

/// <summary>
/// Integration message from Fulfillment to Orders.
/// Published when a shipment is successfully delivered to the customer.
/// </summary>
public sealed record ShipmentDelivered(
    Guid OrderId,
    Guid ShipmentId,
    DateTimeOffset DeliveredAt,
    string? RecipientName = null);
