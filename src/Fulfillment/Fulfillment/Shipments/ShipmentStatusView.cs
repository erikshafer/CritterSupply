using Marten.Events.Aggregation;

namespace Fulfillment.Shipments;

/// <summary>
/// A status history entry for the shipment timeline.
/// </summary>
public sealed record ShipmentStatusEvent(
    string Status,
    string? Description,
    DateTimeOffset OccurredAt);

/// <summary>
/// Customer-facing shipment tracking read model.
/// Implemented as an inline Marten projection spanning the Shipment stream.
/// </summary>
public sealed class ShipmentStatusView
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string Status { get; set; } = "";
    public string? TrackingNumber { get; set; }
    public string? Carrier { get; set; }
    public string? AssignedFulfillmentCenter { get; set; }
    public string? LastScanLocation { get; set; }
    public DateTimeOffset? EstimatedDelivery { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public int DeliveryAttemptCount { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public List<ShipmentStatusEvent> StatusHistory { get; set; } = new();
}

/// <summary>
/// Marten single-stream projection that builds ShipmentStatusView from Shipment events.
/// </summary>
public sealed class ShipmentStatusViewProjection : SingleStreamProjection<ShipmentStatusView, Guid>
{
    public ShipmentStatusView Create(FulfillmentRequested @event)
    {
        var view = new ShipmentStatusView
        {
            OrderId = @event.OrderId,
            Status = "Pending",
            RequestedAt = @event.RequestedAt
        };
        view.StatusHistory.Add(new ShipmentStatusEvent("Pending", "Fulfillment request received", @event.RequestedAt));
        return view;
    }

    public void Apply(FulfillmentCenterAssigned @event, ShipmentStatusView view)
    {
        view.Status = "Assigned";
        view.AssignedFulfillmentCenter = @event.FulfillmentCenterId;
        view.StatusHistory.Add(new ShipmentStatusEvent("Assigned", $"Assigned to {@event.FulfillmentCenterId}", @event.AssignedAt));
    }

    public void Apply(ShippingLabelGenerated @event, ShipmentStatusView view)
    {
        view.Status = "Labeled";
        view.Carrier = @event.Carrier;
        view.StatusHistory.Add(new ShipmentStatusEvent("Labeled", $"Shipping label generated ({@event.Carrier} {@event.Service})", @event.GeneratedAt));
    }

    public void Apply(TrackingNumberAssigned @event, ShipmentStatusView view)
    {
        view.TrackingNumber = @event.TrackingNumber;
        view.Carrier = @event.Carrier;
        view.StatusHistory.Add(new ShipmentStatusEvent("TrackingAssigned", $"Tracking: {@event.TrackingNumber}", @event.AssignedAt));
    }

    public void Apply(ShipmentManifested @event, ShipmentStatusView view)
    {
        view.Status = "Staged";
        view.StatusHistory.Add(new ShipmentStatusEvent("Manifested", $"Manifest {@event.ManifestId}", @event.ManifestTime));
    }

    public void Apply(ShipmentHandedToCarrier @event, ShipmentStatusView view)
    {
        view.Status = "HandedToCarrier";
        view.StatusHistory.Add(new ShipmentStatusEvent("HandedToCarrier", $"Handed to {@event.Carrier}", @event.HandedAt));
    }

    public void Apply(ShipmentInTransit @event, ShipmentStatusView view)
    {
        view.Status = "InTransit";
        view.LastScanLocation = @event.ScanLocation;
        view.StatusHistory.Add(new ShipmentStatusEvent("InTransit", $"Scanned at {@event.ScanLocation}", @event.ScanTime));
    }

    public void Apply(OutForDelivery @event, ShipmentStatusView view)
    {
        view.Status = "OutForDelivery";
        view.EstimatedDelivery = @event.EstimatedDelivery;
        view.StatusHistory.Add(new ShipmentStatusEvent("OutForDelivery", "Out for delivery", @event.ScannedAt));
    }

    public void Apply(ShipmentDelivered @event, ShipmentStatusView view)
    {
        view.Status = "Delivered";
        view.DeliveredAt = @event.DeliveredAt;
        view.StatusHistory.Add(new ShipmentStatusEvent("Delivered", $"Delivered{(@event.RecipientName != null ? $" to {@event.RecipientName}" : "")}", @event.DeliveredAt));
    }

    public void Apply(DeliveryAttemptFailed @event, ShipmentStatusView view)
    {
        view.Status = "DeliveryAttemptFailed";
        view.DeliveryAttemptCount = @event.AttemptNumber;
        view.StatusHistory.Add(new ShipmentStatusEvent("DeliveryAttemptFailed", $"Attempt #{@event.AttemptNumber}: {@event.ExceptionCode}", @event.AttemptDate));
    }

    public void Apply(ReturnToSenderInitiated @event, ShipmentStatusView view)
    {
        view.Status = "ReturningToSender";
        view.StatusHistory.Add(new ShipmentStatusEvent("ReturningToSender", $"Returning via {@event.Carrier} (est. {@event.EstimatedReturnDays} days)", @event.InitiatedAt));
    }

    public void Apply(ReturnReceivedAtWarehouse @event, ShipmentStatusView view)
    {
        view.Status = "ReturnReceived";
        view.StatusHistory.Add(new ShipmentStatusEvent("ReturnReceived", $"Received at {@event.WarehouseId}", @event.ReceivedAt));
    }
}
