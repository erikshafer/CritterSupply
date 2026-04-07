using Messages.Contracts.Fulfillment;
using Storefront.RealTime;
using Wolverine.SignalR;

namespace Storefront.Notifications;

/// <summary>
/// Handles TrackingNumberAssigned integration message from Fulfillment BC.
/// Publishes ShipmentStatusChanged SignalR event — first customer-visible tracking event.
/// Stub handler: the storefront tracking page will render this as "Label created".
/// </summary>
public static class TrackingNumberAssignedHandler
{
    public static SignalRMessage<ShipmentStatusChanged> Handle(TrackingNumberAssigned message)
    {
        // TODO: Query Orders BC for the actual CustomerId so SignalR group targeting works.
        var customerId = Guid.Empty;

        var shipmentStatusChanged = new ShipmentStatusChanged(
            message.ShipmentId,
            message.OrderId,
            customerId,
            "Label created",
            message.TrackingNumber,
            message.AssignedAt);

        return shipmentStatusChanged.ToWebSocketGroup($"customer:{customerId}");
    }
}
