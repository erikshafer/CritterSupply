namespace Fulfillment.Shipments;

/// <summary>
/// Represents the lifecycle status of a shipment.
/// Follows the CONTEXTS.md specification for Fulfillment BC.
/// </summary>
public enum ShipmentStatus
{
    /// <summary>
    /// Fulfillment request received, awaiting warehouse assignment.
    /// </summary>
    Pending,

    /// <summary>
    /// Routed to a specific warehouse/fulfillment center.
    /// </summary>
    Assigned,

    /// <summary>
    /// Items being pulled from bins.
    /// </summary>
    Picking,

    /// <summary>
    /// Items boxed, shipping label generated.
    /// </summary>
    Packing,

    /// <summary>
    /// Handed to carrier, tracking number assigned.
    /// </summary>
    Shipped,

    /// <summary>
    /// Carrier confirmed delivery.
    /// </summary>
    Delivered,

    /// <summary>
    /// Delivery attempted but unsuccessful.
    /// </summary>
    DeliveryFailed
}
