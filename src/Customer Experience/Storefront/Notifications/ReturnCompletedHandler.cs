using Storefront.RealTime;
using Wolverine.SignalR;

namespace Storefront.Notifications;

/// <summary>
/// Handles Returns.ReturnCompleted integration message and publishes SignalR update via Wolverine.
/// Notifies customer that their return has been approved after inspection and refund issued.
/// </summary>
public static class ReturnCompletedHandler
{
    public static SignalRMessage<ReturnStatusChanged> Handle(Messages.Contracts.Returns.ReturnCompleted message)
    {
        var returnStatusChanged = new ReturnStatusChanged(
            message.ReturnId,
            message.OrderId,
            message.CustomerId,
            "Completed",
            $"Your return has been processed. Refund of {message.FinalRefundAmount:C} has been issued.",
            message.CompletedAt);

        // Send only to the authenticated customer's group — not broadcast to all clients
        return returnStatusChanged.ToWebSocketGroup($"customer:{message.CustomerId}");
    }
}
