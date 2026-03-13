using Storefront.RealTime;
using Wolverine.SignalR;

namespace Storefront.Notifications;

/// <summary>
/// Handles Returns.ReturnRejected integration message and publishes SignalR update via Wolverine.
/// Notifies customer that their return failed inspection and refund was denied.
/// </summary>
public static class ReturnRejectedHandler
{
    public static SignalRMessage<ReturnStatusChanged> Handle(Messages.Contracts.Returns.ReturnRejected message)
    {
        var returnStatusChanged = new ReturnStatusChanged(
            message.ReturnId,
            message.OrderId,
            message.CustomerId,
            "Rejected",
            $"Your return did not pass inspection: {message.Reason}. No refund will be issued.",
            message.RejectedAt);

        // Send only to the authenticated customer's group — not broadcast to all clients
        return returnStatusChanged.ToWebSocketGroup($"customer:{message.CustomerId}");
    }
}
