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
        Identity<FulfillmentRequested>(e => e.OrderId);
        Identity<TrackingNumberAssigned>(_ => Guid.Empty); // Simplified — does not carry OrderId directly
        Identity<ShipmentDelivered>(_ => Guid.Empty); // Simplified — does not carry OrderId directly
        Identity<ReshipmentCreated>(_ => Guid.Empty); // Simplified — does not carry OrderId directly
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
        var entry = view.Shipments.FirstOrDefault(s => s.TrackingNumber == null);
        if (entry != null)
        {
            var idx = view.Shipments.IndexOf(entry);
            view.Shipments[idx] = entry with { TrackingNumber = @event.TrackingNumber };
        }
    }

    public void Apply(ShipmentDelivered _, MultiShipmentView view)
    {
        // Update the latest non-delivered entry
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
