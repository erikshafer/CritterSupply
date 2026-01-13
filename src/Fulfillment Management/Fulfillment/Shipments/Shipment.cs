namespace Fulfillment.Shipments;

/// <summary>
/// Event-sourced aggregate representing a shipment fulfillment workflow.
/// Follows immutable pattern with pure functions for applying events.
/// </summary>
public sealed record Shipment(
    Guid Id,
    Guid OrderId,
    Guid CustomerId,
    ShippingAddress ShippingAddress,
    IReadOnlyList<FulfillmentLineItem> LineItems,
    string ShippingMethod,
    ShipmentStatus Status,
    string? WarehouseId,
    string? Carrier,
    string? TrackingNumber,
    DateTimeOffset RequestedAt,
    DateTimeOffset? AssignedAt,
    DateTimeOffset? DispatchedAt,
    DateTimeOffset? DeliveredAt,
    string? FailureReason)
{
    /// <summary>
    /// Create initial state from FulfillmentRequested event.
    /// This is used by Marten to create the aggregate from the first event in the stream.
    /// </summary>
    public static Shipment Create(FulfillmentRequested @event) =>
        new(Guid.CreateVersion7(),
            @event.OrderId,
            @event.CustomerId,
            @event.ShippingAddress,
            @event.LineItems,
            @event.ShippingMethod,
            ShipmentStatus.Pending,
            null,
            null,
            null,
            @event.RequestedAt,
            null,
            null,
            null,
            null);

    /// <summary>
    /// Apply warehouse assignment event.
    /// </summary>
    public Shipment Apply(WarehouseAssigned @event) =>
        this with
        {
            Status = ShipmentStatus.Assigned,
            WarehouseId = @event.WarehouseId,
            AssignedAt = @event.AssignedAt
        };

    /// <summary>
    /// Apply shipment dispatched event.
    /// </summary>
    public Shipment Apply(ShipmentDispatched @event) =>
        this with
        {
            Status = ShipmentStatus.Shipped,
            Carrier = @event.Carrier,
            TrackingNumber = @event.TrackingNumber,
            DispatchedAt = @event.DispatchedAt
        };

    /// <summary>
    /// Apply shipment delivered event.
    /// </summary>
    public Shipment Apply(ShipmentDelivered @event) =>
        this with
        {
            Status = ShipmentStatus.Delivered,
            DeliveredAt = @event.DeliveredAt
        };

    /// <summary>
    /// Apply delivery failure event.
    /// </summary>
    public Shipment Apply(ShipmentDeliveryFailed @event) =>
        this with
        {
            Status = ShipmentStatus.DeliveryFailed,
            FailureReason = @event.Reason
        };
}
