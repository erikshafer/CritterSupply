using Storefront.RealTime;
using Wolverine.SignalR;

namespace Storefront.Notifications;

/// <summary>
/// Handles <c>Returns.ExchangeAdditionalPaymentCaptured</c> integration
/// message and publishes a SignalR update via Wolverine. Tells the
/// customer in real time that the price-difference delta for their
/// more-expensive cross-product replacement has been billed.
///
/// <para>
/// Lifts the structured payment metadata added in M47.0 / Slice 2
/// (<c>PaymentId</c>, <c>Currency</c>, <c>PaymentReference</c>) onto the
/// <see cref="ReturnExchangePaymentChanged"/> SignalR event so the
/// storefront can render currency-aware copy and a selectable payment
/// reference without re-querying Payments. Scoped to the customer-only
/// SignalR group — never broadcast.
/// </para>
/// </summary>
public static class ExchangeAdditionalPaymentCapturedHandler
{
    public static SignalRMessage<ReturnExchangePaymentChanged> Handle(
        Messages.Contracts.Returns.ExchangeAdditionalPaymentCaptured message)
    {
        var payload = new ReturnExchangePaymentChanged(
            message.ReturnId,
            message.OrderId,
            message.CustomerId,
            PaymentKind: "Capture",
            message.PaymentId,
            message.AmountCaptured,
            message.Currency,
            message.PaymentReference,
            message.CapturedAt);

        return payload.ToWebSocketGroup($"customer:{message.CustomerId}");
    }
}
