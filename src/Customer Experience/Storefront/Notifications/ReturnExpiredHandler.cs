using Storefront.RealTime;
using Wolverine.SignalR;

namespace Storefront.Notifications;

/// <summary>
/// Handles Returns.ReturnExpired integration message and publishes SignalR update via Wolverine.
/// Notifies customer that their approved return has expired due to missing shipping deadline.
/// </summary>
public static class ReturnExpiredHandler
{
    public static SignalRMessage<ReturnStatusChanged> Handle(Messages.Contracts.Returns.ReturnExpired message)
    {
        var returnStatusChanged = new ReturnStatusChanged(
            message.ReturnId,
            message.OrderId,
            message.CustomerId,
            "Expired",
            "Your return authorization has expired. You did not ship the item within the required timeframe.",
            message.ExpiredAt);

        // Send only to the authenticated customer's group — not broadcast to all clients
        return returnStatusChanged.ToWebSocketGroup($"customer:{message.CustomerId}");
    }
}
