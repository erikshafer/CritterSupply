using Marten;
using Wolverine;
using PaymentsMessages = Messages.Contracts.Payments;

namespace Payments.Processing;

/// <summary>
/// M47.0 / Slice 4 — refunds the captured additional-payment delta when
/// inspection rejects a cross-product exchange that had already moved
/// money. Listens for <see cref="PaymentsMessages.RefundExchangeDeltaRequested"/>
/// and replies with <see cref="PaymentsMessages.ExchangePartialRefundIssued"/>
/// (the same reply contract used by
/// <see cref="IssueExchangePartialRefundHandler"/> for cheaper-replacement
/// refunds — Storefront / Orders / Backoffice already render it, no new
/// downstream wiring needed).
///
/// <para>
/// Targets the Slice 2 delta-capture <see cref="Payment"/> stream
/// (deterministic id from <see cref="ExchangePaymentIds.ComputeDeltaPaymentId"/>),
/// not the original Order payment. The refund is full ( = the captured
/// amount).
/// </para>
///
/// <para>
/// Idempotency: under at-least-once redelivery, a second invocation with
/// the same <c>ReturnId</c> finds the existing
/// <see cref="PaymentRefunded"/> event keyed on <c>ReturnId</c> and
/// re-emits the success reply without calling the gateway a second time
/// or appending further events.
/// </para>
///
/// <para>
/// Failure modes:
/// <list type="bullet">
///   <item>Delta payment stream missing — the inspection-rejection path
///     should never publish a refund request without a captured delta,
///     but a defensive log + silent return keeps the Slice 4 contract
///     symmetric with <see cref="IssueExchangePartialRefundHandler"/>.</item>
///   <item>Delta payment not in <see cref="PaymentStatus.Captured"/> —
///     same defensive log + silent return.</item>
///   <item>Gateway decline — silent return, leaving the refund request
///     replayable. (Mirrors Slice 2 partial-refund failure semantics.)</item>
/// </list>
/// </para>
/// </summary>
public static class RefundExchangeDeltaHandler
{
    public static async Task<OutgoingMessages> HandleAsync(
        PaymentsMessages.RefundExchangeDeltaRequested message,
        IDocumentSession session,
        IPaymentGateway gateway,
        CancellationToken cancellationToken)
    {
        var outgoing = new OutgoingMessages();

        var deltaPaymentId = ExchangePaymentIds.ComputeDeltaPaymentId(message.ReturnId);
        var deltaPayment = await session.LoadAsync<Payment>(deltaPaymentId, cancellationToken);

        if (deltaPayment is null) return outgoing;
        if (deltaPayment.Status != PaymentStatus.Captured) return outgoing;

        // Idempotency check — has a refund for this ReturnId already been
        // applied to the delta-payment stream?
        var existingEvents = await session.Events.FetchStreamAsync(deltaPaymentId, token: cancellationToken);
        var alreadyRefunded = existingEvents
            .Select(e => e.Data)
            .OfType<PaymentRefunded>()
            .FirstOrDefault(r => r.ReturnId == message.ReturnId);

        if (alreadyRefunded is not null)
        {
            outgoing.Add(new PaymentsMessages.ExchangePartialRefundIssued(
                ReturnId: message.ReturnId,
                OrderId: message.OrderId,
                OriginalPaymentId: deltaPaymentId,
                RefundAmount: alreadyRefunded.RefundAmount,
                Currency: deltaPayment.Currency,
                TransactionId: alreadyRefunded.RefundTransactionId,
                IssuedAt: alreadyRefunded.RefundedAt));
            return outgoing;
        }

        var gatewayResult = await gateway.RefundAsync(
            deltaPayment.TransactionId ?? string.Empty,
            message.RefundAmount,
            cancellationToken);

        if (!gatewayResult.Success)
        {
            // Slice 4: refund-side gateway failures are out of scope for
            // customer-visible compensation — leaving the request replayable
            // by ops. Mirrors IssueExchangePartialRefundHandler.
            return outgoing;
        }

        var now = DateTimeOffset.UtcNow;
        var refundedEvent = new PaymentRefunded(
            PaymentId: deltaPaymentId,
            RefundAmount: message.RefundAmount,
            TotalRefunded: deltaPayment.TotalRefunded + message.RefundAmount,
            RefundTransactionId: gatewayResult.TransactionId!,
            RefundedAt: now,
            ReturnId: message.ReturnId);

        session.Events.Append(deltaPaymentId, refundedEvent);

        outgoing.Add(new PaymentsMessages.ExchangePartialRefundIssued(
            ReturnId: message.ReturnId,
            OrderId: message.OrderId,
            OriginalPaymentId: deltaPaymentId,
            RefundAmount: message.RefundAmount,
            Currency: deltaPayment.Currency,
            TransactionId: gatewayResult.TransactionId!,
            IssuedAt: now));

        return outgoing;
    }
}
