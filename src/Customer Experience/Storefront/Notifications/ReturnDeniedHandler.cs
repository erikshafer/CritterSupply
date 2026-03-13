using Storefront.RealTime;
using Wolverine.SignalR;

namespace Storefront.Notifications;

/// <summary>
/// Handles Returns.ReturnDenied integration message and publishes SignalR update via Wolverine.
/// Notifies customer that their return request has been denied with reason.
/// </summary>
public static class ReturnDeniedHandler
{
    public static SignalRMessage<ReturnStatusChanged> Handle(Messages.Contracts.Returns.ReturnDenied message)
    {
        var details = string.IsNullOrWhiteSpace(message.Message)
            ? $"Denial reason: {message.Reason}"
            : $"{message.Reason}: {message.Message}";

        var returnStatusChanged = new ReturnStatusChanged(
            message.ReturnId,
            message.OrderId,
            message.CustomerId,
            "Denied",
            details,
            message.DeniedAt);

        // Send only to the authenticated customer's group — not broadcast to all clients
        return returnStatusChanged.ToWebSocketGroup($"customer:{message.CustomerId}");
    }
}
