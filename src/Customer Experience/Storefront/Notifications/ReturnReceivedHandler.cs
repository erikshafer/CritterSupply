using Storefront.RealTime;
using Wolverine.SignalR;

namespace Storefront.Notifications;

/// <summary>
/// Handles Returns.ReturnReceived integration message and publishes SignalR update via Wolverine.
/// Notifies customer that their return package has been received at the warehouse.
/// Reduces customer anxiety by confirming physical receipt before inspection.
/// </summary>
public static class ReturnReceivedHandler
{
    public static SignalRMessage<ReturnStatusChanged> Handle(Messages.Contracts.Returns.ReturnReceived message)
    {
        var returnStatusChanged = new ReturnStatusChanged(
            message.ReturnId,
            message.OrderId,
            message.CustomerId,
            "Received",
            "We've received your return package and will inspect it shortly.",
            message.ReceivedAt);

        // Send only to the authenticated customer's group — not broadcast to all clients
        return returnStatusChanged.ToWebSocketGroup($"customer:{message.CustomerId}");
    }
}
