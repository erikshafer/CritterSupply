using Storefront.RealTime;
using Wolverine.SignalR;

namespace Storefront.Notifications;

/// <summary>
/// Handles Returns.ReturnRequested integration message and publishes SignalR update via Wolverine.
/// Notifies customer that their return request has been initiated and is pending review.
/// </summary>
public static class ReturnRequestedHandler
{
    public static SignalRMessage<ReturnStatusChanged> Handle(Messages.Contracts.Returns.ReturnRequested message)
    {
        var returnStatusChanged = new ReturnStatusChanged(
            message.ReturnId,
            message.OrderId,
            message.CustomerId,
            "Requested",
            "Your return request has been submitted and is pending review.",
            message.RequestedAt);

        // Send only to the authenticated customer's group — not broadcast to all clients
        return returnStatusChanged.ToWebSocketGroup($"customer:{message.CustomerId}");
    }
}
