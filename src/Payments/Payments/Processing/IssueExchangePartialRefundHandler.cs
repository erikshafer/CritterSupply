using Marten;
using Wolverine;
using PaymentsMessages = Messages.Contracts.Payments;

namespace Payments.Processing;

/// <summary>
/// Issues the partial refund for a cross-product exchange where the
/// replacement costs less than the original. Listens for
/// <see cref="PaymentsMessages.ExchangePartialRefundRequested"/>
/// (M47.0 / Slice 2) and replies with
/// <see cref="PaymentsMessages.ExchangePartialRefundIssued"/> on success.
///
/// <para>
/// The refund is applied to the original captured <see cref="Payment"/>
/// for the same <c>OrderId</c>. The handler appends a
/// <see cref="PaymentRefunded"/> event to the original Payment stream
/// (no new stream); the event carries the <c>ReturnId</c> as its
/// idempotency key. See ADR 0062.
/// </para>
///
/// <para>
/// Idempotency: under at-least-once redelivery, a second invocation with
/// the same <c>ReturnId</c> finds an existing <see cref="PaymentRefunded"/>
/// event for that ReturnId on the original stream and re-emits the
/// success reply without calling the gateway a second time.
/// </para>
///
/// <para>
/// Slice 2 scope is the happy path only. If the original captured payment
/// cannot be located, the handler logs and emits no reply — Slice 4 will
/// surface this as a customer-visible failure.
/// </para>
/// </summary>
public static class IssueExchangePartialRefundHandler
{
    public static async Task<OutgoingMessages> HandleAsync(
        PaymentsMessages.ExchangePartialRefundRequested message,
        IDocumentSession session,
        IPaymentGateway gateway,
        CancellationToken cancellationToken)
    {
        var outgoing = new OutgoingMessages();

        var original = await session.Query<Payment>()
            .Where(p => p.OrderId == message.OrderId && p.Status == PaymentStatus.Captured)
            .OrderBy(p => p.InitiatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (original is null)
        {
            // No captured original. Slice 2 leaves this without a reply;
            // Slice 4 will turn it into a customer-visible failure path.
            // Returning empty keeps the message off the no-handler log.
            return outgoing;
        }

        // ----------------------------------------------------------------
        // Idempotency check — has a refund for this ReturnId already been
        // applied to the original Payment stream?
        // ----------------------------------------------------------------
        var existingEvents = await session.Events.FetchStreamAsync(original.Id, token: cancellationToken);
        var alreadyRefunded = existingEvents
            .Select(e => e.Data)
            .OfType<PaymentRefunded>()
            .FirstOrDefault(r => r.ReturnId == message.ReturnId);

        if (alreadyRefunded is not null)
        {
            outgoing.Add(new PaymentsMessages.ExchangePartialRefundIssued(
                ReturnId: message.ReturnId,
                OrderId: message.OrderId,
                OriginalPaymentId: original.Id,
                RefundAmount: alreadyRefunded.RefundAmount,
                Currency: original.Currency,
                TransactionId: alreadyRefunded.RefundTransactionId,
                IssuedAt: alreadyRefunded.RefundedAt));
            return outgoing;
        }

        var gatewayResult = await gateway.RefundAsync(
            original.TransactionId ?? string.Empty,
            message.RefundAmount,
            cancellationToken);

        if (!gatewayResult.Success)
        {
            // Slice 2: refund failure surfaces to the log only. Slice 4
            // owns the customer-visible failure path. The Returns
            // aggregate stays in Completed without a recorded refund;
            // ops can replay once the gateway recovers.
            return outgoing;
        }

        var now = DateTimeOffset.UtcNow;
        var refundedEvent = new PaymentRefunded(
            PaymentId: original.Id,
            RefundAmount: message.RefundAmount,
            TotalRefunded: original.TotalRefunded + message.RefundAmount,
            RefundTransactionId: gatewayResult.TransactionId!,
            RefundedAt: now,
            ReturnId: message.ReturnId);

        session.Events.Append(original.Id, refundedEvent);

        outgoing.Add(new PaymentsMessages.ExchangePartialRefundIssued(
            ReturnId: message.ReturnId,
            OrderId: message.OrderId,
            OriginalPaymentId: original.Id,
            RefundAmount: message.RefundAmount,
            Currency: original.Currency,
            TransactionId: gatewayResult.TransactionId!,
            IssuedAt: now));

        return outgoing;
    }
}
