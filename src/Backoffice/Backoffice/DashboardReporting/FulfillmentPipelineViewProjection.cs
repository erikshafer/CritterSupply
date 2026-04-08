using Marten.Events.Projections;
using Messages.Contracts.Fulfillment;

namespace Backoffice.DashboardReporting;

/// <summary>
/// Inline projection that builds FulfillmentPipelineView from Fulfillment BC integration messages.
/// Tracks active shipments in transit, delivered, failed, backordered, and lost.
/// Updated in M41.0 S5 to consume new Fulfillment event surface.
/// </summary>
public sealed class FulfillmentPipelineViewProjection : MultiStreamProjection<FulfillmentPipelineView, string>
{
    public FulfillmentPipelineViewProjection()
    {
        // All Fulfillment events update the singleton "current" document
        Identity<ShipmentHandedToCarrier>(_ => "current");
        Identity<ShipmentDelivered>(_ => "current");
        Identity<ReturnToSenderInitiated>(_ => "current");
        Identity<BackorderCreated>(_ => "current");
        Identity<ShipmentLostInTransit>(_ => "current");
    }

    /// <summary>
    /// Initialize the view when the first ShipmentHandedToCarrier event arrives.
    /// </summary>
    public FulfillmentPipelineView Create(ShipmentHandedToCarrier evt)
    {
        return new FulfillmentPipelineView
        {
            Id = "current",
            ShipmentsInTransit = 1,
            ShipmentsDelivered = 0,
            DeliveryFailures = 0,
            Backorders = 0,
            ShipmentsLostInTransit = 0,
            LastUpdatedAt = evt.HandedAt
        };
    }

    /// <summary>
    /// Increment in-transit count when a shipment is handed to carrier.
    /// </summary>
    public static FulfillmentPipelineView Apply(FulfillmentPipelineView current, ShipmentHandedToCarrier evt)
    {
        return current with
        {
            ShipmentsInTransit = current.ShipmentsInTransit + 1,
            LastUpdatedAt = evt.HandedAt
        };
    }

    /// <summary>
    /// Move shipment from in-transit to delivered when delivery succeeds.
    /// </summary>
    public static FulfillmentPipelineView Apply(FulfillmentPipelineView current, ShipmentDelivered evt)
    {
        return current with
        {
            ShipmentsInTransit = current.ShipmentsInTransit - 1,
            ShipmentsDelivered = current.ShipmentsDelivered + 1,
            LastUpdatedAt = evt.DeliveredAt
        };
    }

    /// <summary>
    /// Move shipment from in-transit to failed when return-to-sender is initiated.
    /// </summary>
    public static FulfillmentPipelineView Apply(FulfillmentPipelineView current, ReturnToSenderInitiated evt)
    {
        return current with
        {
            ShipmentsInTransit = current.ShipmentsInTransit - 1,
            DeliveryFailures = current.DeliveryFailures + 1,
            LastUpdatedAt = evt.InitiatedAt
        };
    }

    /// <summary>
    /// Increment backorder count when a shipment is backordered.
    /// </summary>
    public static FulfillmentPipelineView Apply(FulfillmentPipelineView current, BackorderCreated evt)
    {
        return current with
        {
            Backorders = current.Backorders + 1,
            LastUpdatedAt = evt.CreatedAt
        };
    }

    /// <summary>
    /// Increment lost-in-transit count (shipment moves from in-transit to lost).
    /// </summary>
    public static FulfillmentPipelineView Apply(FulfillmentPipelineView current, ShipmentLostInTransit evt)
    {
        return current with
        {
            ShipmentsInTransit = current.ShipmentsInTransit - 1,
            ShipmentsLostInTransit = current.ShipmentsLostInTransit + 1,
            LastUpdatedAt = evt.DetectedAt
        };
    }
}
