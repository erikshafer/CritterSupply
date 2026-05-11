using Storefront.RealTime;
using Wolverine.SignalR;

namespace Storefront.Notifications;

/// <summary>
/// M47.0 / Slice 4 — handles <c>Messages.Contracts.Returns.ExchangeCancelled</c>
/// and publishes a SignalR <see cref="ReturnStatusChanged"/> with the
/// new <c>"Cancelled"</c> status. The customer-facing details string is
/// the verbatim PO-approved copy carried on the integration message
/// (<c>"Payment for price difference could not be processed.
/// Exchange cancelled."</c>).
///
/// <para>
/// Mirrors the existing 8 Returns notification handlers — group-scoped
/// to <c>customer:{CustomerId}</c>, never broadcast.
/// </para>
/// </summary>
public static class ExchangeCancelledHandler
{
    public static SignalRMessage<ReturnStatusChanged> Handle(
        Messages.Contracts.Returns.ExchangeCancelled message)
    {
        var returnStatusChanged = new ReturnStatusChanged(
            message.ReturnId,
            message.OrderId,
            message.CustomerId,
            "Cancelled",
            message.Message,
            message.CancelledAt);

        return returnStatusChanged.ToWebSocketGroup($"customer:{message.CustomerId}");
    }
}
