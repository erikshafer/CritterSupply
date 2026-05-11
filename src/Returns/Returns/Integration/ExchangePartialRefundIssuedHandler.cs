using Marten;
using Returns.ReturnProcessing;
using Wolverine;
using PaymentsMessages = Messages.Contracts.Payments;
using ReturnsMessages = Messages.Contracts.Returns;

namespace Returns.Integration;

/// <summary>
/// Consumes the Payments BC's success reply for a cross-product exchange
/// partial refund. See ADR 0062 and
/// <c>docs/planning/milestones/m47-0-plan.md</c>.
///
/// <para>
/// On <see cref="PaymentsMessages.ExchangePartialRefundIssued"/>, this
/// handler appends the <see cref="ExchangePartialRefundIssued"/>
/// domain event to the Return stream and republishes the public
/// <see cref="ReturnsMessages.ExchangePartialRefundIssued"/> for the
/// Storefront / Orders / Backoffice fan-out.
/// </para>
///
/// <para>
/// Idempotency: only appends + publishes when the aggregate is a
/// cross-product exchange in the Completed state with no recorded final
/// refund yet. Re-deliveries are no-ops.
/// </para>
/// </summary>
public static class ExchangePartialRefundIssuedHandler
{
    public static async Task Handle(
        PaymentsMessages.ExchangePartialRefundIssued message,
        IDocumentSession session,
        IMessageBus bus,
        CancellationToken ct)
    {
        var stream = await session.Events.FetchForWriting<Return>(message.ReturnId, ct);
        var aggregate = stream.Aggregate;

        if (aggregate is null) return;
        if (!aggregate.IsCrossProductExchange) return;

        // Idempotency on a Slice 2-specific signal. We can't use
        // `aggregate.FinalRefundAmount` because `ShipReplacementItemHandler` already
        // sets it via `ExchangeCompleted.PriceDifferenceRefund` as a precondition of
        // this choreography — every cheaper-replacement exchange enters this handler
        // with a non-null FinalRefundAmount. Instead, scan the Return event stream
        // for an existing `ExchangePartialRefundIssued` domain event (mirrors the
        // Payments side's `PaymentRefunded.ReturnId` lookup in
        // `IssueExchangePartialRefundHandler`). This was reported by QA against the
        // first M47.0 / S2 PSA-cut and is the fix.
        //
        // Full-stream fetch is acceptable here: Return aggregates are bounded
        // (~10-30 events for the full lifecycle including approval, receipt,
        // inspection, replacement shipment) and `FetchForWriting` above already
        // hydrated the same stream, so the second fetch is materially served
        // from Marten's identity map / unit-of-work rather than re-reading
        // every event row from Postgres. If Return streams ever grow
        // unbounded (subscription-style returns, multi-year retention),
        // revisit with `session.Events.QueryAllRawEvents().Where(...).AnyAsync()`.
        var existingEvents = await session.Events.FetchStreamAsync(message.ReturnId, token: ct);
        var alreadyApplied = existingEvents
            .Any(e => e.Data is ExchangePartialRefundIssued);
        if (alreadyApplied) return;

        var domainEvent = new ExchangePartialRefundIssued(
            ReturnId: message.ReturnId,
            RefundAmount: message.RefundAmount,
            IssuedAt: message.IssuedAt);

        stream.AppendOne(domainEvent);

        await bus.PublishAsync(new ReturnsMessages.ExchangePartialRefundIssued(
            ReturnId: message.ReturnId,
            OrderId: aggregate.OrderId,
            CustomerId: aggregate.CustomerId,
            OriginalPaymentId: message.OriginalPaymentId,
            RefundAmount: message.RefundAmount,
            Currency: message.Currency,
            TransactionId: message.TransactionId,
            IssuedAt: message.IssuedAt));
    }
}
