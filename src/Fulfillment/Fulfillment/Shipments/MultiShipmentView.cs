using Marten.Events.Projections;

namespace Fulfillment.Shipments;

/// <summary>
/// Entry for a shipment within a multi-shipment order.
/// </summary>
public sealed record ShipmentEntry(
    Guid ShipmentId,
    string Status,
    string? TrackingNumber,
    bool IsReshipment,
    Guid? OriginalShipmentId);

/// <summary>
/// Multi-shipment tracking read model, keyed by OrderId.
/// Required by Slice 32 (split orders) and reshipment tracking (Slice 30).
/// </summary>
public sealed class MultiShipmentView
{
    public Guid Id { get; set; }
    public List<ShipmentEntry> Shipments { get; set; } = new();
}

/// <summary>
/// Multi-stream projection that builds MultiShipmentView from shipment lifecycle events.
/// Keyed by OrderId.
/// </summary>
public sealed class MultiShipmentViewProjection : MultiStreamProjection<MultiShipmentView, Guid>
{
    public MultiShipmentViewProjection()
    {
        // All four events now carry OrderId directly — projection no longer
        // needs to fall back to Guid.Empty (which previously bucketed every
        // tracking/delivery/reshipment update across all orders into a single
        // "Empty" view document — see fulfillment-remaster-s3-retrospective.md
        // gaps #2 and #3, and the M45.0 retrospective).
        Identity<FulfillmentRequested>(e => e.OrderId);
        Identity<TrackingNumberAssigned>(e => e.OrderId);
        Identity<ShipmentDelivered>(e => e.OrderId);
        Identity<ReshipmentCreated>(e => e.OrderId);
    }

    public MultiShipmentView Create(FulfillmentRequested @event) =>
        new() { Id = @event.OrderId };

    public void Apply(FulfillmentRequested @event, MultiShipmentView view)
    {
        // Add shipment entry if not already tracked
        var shipmentId = Shipment.StreamId(@event.OrderId);
        if (view.Shipments.All(s => s.ShipmentId != shipmentId))
        {
            view.Shipments.Add(new ShipmentEntry(
                shipmentId, "Pending", null, false, null));
        }
    }

    public void Apply(TrackingNumberAssigned @event, MultiShipmentView view)
    {
        // Update the canonical (non-reshipment) entry first when its tracking
        // number is still unassigned; otherwise the most recent reshipment
        // entry that's still missing tracking. Either way, scope is bounded
        // to this view's OrderId since the projection is now properly keyed.
        var entry = view.Shipments.FirstOrDefault(s => s.TrackingNumber == null);
        if (entry != null)
        {
            var idx = view.Shipments.IndexOf(entry);
            view.Shipments[idx] = entry with { TrackingNumber = @event.TrackingNumber };
        }
    }

    public void Apply(ShipmentDelivered _, MultiShipmentView view)
    {
        // Update the latest non-delivered entry within this order's view.
        var entry = view.Shipments.FirstOrDefault(s => s.Status != "Delivered");
        if (entry != null)
        {
            var idx = view.Shipments.IndexOf(entry);
            view.Shipments[idx] = entry with { Status = "Delivered" };
        }
    }

    public void Apply(ReshipmentCreated @event, MultiShipmentView view)
    {
        view.Shipments.Add(new ShipmentEntry(
            @event.NewShipmentId, "Pending", null, true, @event.OriginalShipmentId));
    }
}
