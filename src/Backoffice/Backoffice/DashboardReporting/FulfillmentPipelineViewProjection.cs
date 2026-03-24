using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Messages.Contracts.Fulfillment;

namespace Backoffice.DashboardReporting;

/// <summary>
/// Inline projection that builds FulfillmentPipelineView from Fulfillment BC integration messages.
/// Tracks active shipments in transit, delivered shipments, and failed delivery attempts.
/// </summary>
public sealed class FulfillmentPipelineViewProjection : MultiStreamProjection<FulfillmentPipelineView, string>
{
    public FulfillmentPipelineViewProjection()
    {
        // All Fulfillment events update the singleton "current" document
        Identity<ShipmentDispatched>(_ => "current");
        Identity<ShipmentDelivered>(_ => "current");
        Identity<ShipmentDeliveryFailed>(_ => "current");
    }

    /// <summary>
    /// Initialize the view when the first ShipmentDispatched event arrives.
    /// </summary>
    public FulfillmentPipelineView Create(ShipmentDispatched evt)
    {
        return new FulfillmentPipelineView
        {
            Id = "current",
            ShipmentsInTransit = 1,
            ShipmentsDelivered = 0,
            DeliveryFailures = 0,
            LastUpdatedAt = evt.DispatchedAt
        };
    }

    /// <summary>
    /// Increment in-transit count when a shipment is dispatched.
    /// </summary>
    public static FulfillmentPipelineView Apply(FulfillmentPipelineView current, ShipmentDispatched evt)
    {
        return current with
        {
            ShipmentsInTransit = current.ShipmentsInTransit + 1,
            LastUpdatedAt = evt.DispatchedAt
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
    /// Move shipment from in-transit to failed when delivery fails.
    /// </summary>
    public static FulfillmentPipelineView Apply(FulfillmentPipelineView current, ShipmentDeliveryFailed evt)
    {
        return current with
        {
            ShipmentsInTransit = current.ShipmentsInTransit - 1,
            DeliveryFailures = current.DeliveryFailures + 1,
            LastUpdatedAt = evt.FailedAt
        };
    }
}
