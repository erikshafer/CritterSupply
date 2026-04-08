using Messages.Contracts.Fulfillment;
using Storefront.RealTime;
using Wolverine.SignalR;

namespace Storefront.Notifications;

/// <summary>
/// Handles ReturnToSenderInitiated integration message from Fulfillment BC.
/// Publishes ShipmentStatusChanged SignalR event to customer's UI via Wolverine.
/// Replaces ShipmentDeliveryFailedHandler (retired in M41.0 S5).
/// </summary>
public static class ReturnToSenderInitiatedHandler
{
    public static SignalRMessage<ShipmentStatusChanged> Handle(ReturnToSenderInitiated message)
    {
        // TODO: Query Orders BC for the actual CustomerId so SignalR group targeting works.
        var customerId = Guid.Empty;

        var shipmentStatusChanged = new ShipmentStatusChanged(
            message.ShipmentId,
            message.OrderId,
            customerId,
            "Returning to sender",
            null,
            message.InitiatedAt);

        return shipmentStatusChanged.ToWebSocketGroup($"customer:{customerId}");
    }
}
