using Storefront.RealTime;
using Wolverine.SignalR;

namespace Storefront.Notifications;

/// <summary>
/// Handles Returns.ReturnApproved integration message and publishes SignalR update via Wolverine.
/// Notifies customer that their return has been approved and provides shipping deadline.
/// </summary>
public static class ReturnApprovedHandler
{
    public static SignalRMessage<ReturnStatusChanged> Handle(Messages.Contracts.Returns.ReturnApproved message)
    {
        var returnStatusChanged = new ReturnStatusChanged(
            message.ReturnId,
            message.OrderId,
            message.CustomerId,
            "Approved",
            $"Your return has been approved. Please ship by {message.ShipByDeadline:MMM dd, yyyy}. " +
            $"Estimated refund: {message.EstimatedRefundAmount:C} (after {message.RestockingFeeAmount:C} restocking fee).",
            message.ApprovedAt);

        // Send only to the authenticated customer's group — not broadcast to all clients
        return returnStatusChanged.ToWebSocketGroup($"customer:{message.CustomerId}");
    }
}
