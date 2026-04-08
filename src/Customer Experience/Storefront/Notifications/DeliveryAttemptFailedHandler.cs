using Messages.Contracts.Fulfillment;
using Storefront.RealTime;
using Wolverine.SignalR;

namespace Storefront.Notifications;

/// <summary>
/// Handles DeliveryAttemptFailed integration message from Fulfillment BC.
/// Publishes ShipmentStatusChanged SignalR event — notifies customer of failed delivery attempt.
/// Stub handler: the storefront tracking page will render attempt info and action guidance.
/// </summary>
public static class DeliveryAttemptFailedHandler
{
    public static SignalRMessage<ShipmentStatusChanged> Handle(DeliveryAttemptFailed message)
    {
        // TODO: Query Orders BC for the actual CustomerId so SignalR group targeting works.
        var customerId = Guid.Empty;

        var status = message.AttemptNumber >= 3
            ? "Final delivery attempt failed"
            : $"Delivery attempt {message.AttemptNumber} failed";

        var shipmentStatusChanged = new ShipmentStatusChanged(
            message.ShipmentId,
            message.OrderId,
            customerId,
            status,
            null,
            message.AttemptDate);

        return shipmentStatusChanged.ToWebSocketGroup($"customer:{customerId}");
    }
}
