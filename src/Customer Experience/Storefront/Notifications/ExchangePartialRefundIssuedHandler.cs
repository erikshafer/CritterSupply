using Storefront.RealTime;
using Wolverine.SignalR;

namespace Storefront.Notifications;

/// <summary>
/// Handles <c>Returns.ExchangePartialRefundIssued</c> integration message
/// and publishes a SignalR update via Wolverine. Tells the customer in
/// real time that the price-difference partial refund for their
/// cheaper cross-product replacement has been issued back to their
/// original payment method.
///
/// <para>
/// Lifts the structured payment metadata added in M47.0 / Slice 2
/// (<c>OriginalPaymentId</c>, <c>Currency</c>, <c>TransactionId</c>) onto
/// the <see cref="ReturnExchangePaymentChanged"/> SignalR event. The
/// gateway transaction id is mapped to <c>PaymentReference</c> on the
/// SignalR shape so capture and refund flows share a single typed event
/// over a single discriminator. Scoped to the customer-only SignalR
/// group — never broadcast.
/// </para>
/// </summary>
public static class ExchangePartialRefundIssuedHandler
{
    public static SignalRMessage<ReturnExchangePaymentChanged> Handle(
        Messages.Contracts.Returns.ExchangePartialRefundIssued message)
    {
        var payload = new ReturnExchangePaymentChanged(
            message.ReturnId,
            message.OrderId,
            message.CustomerId,
            PaymentKind: "Refund",
            message.OriginalPaymentId,
            message.RefundAmount,
            message.Currency,
            message.TransactionId,
            message.IssuedAt);

        return payload.ToWebSocketGroup($"customer:{message.CustomerId}");
    }
}
