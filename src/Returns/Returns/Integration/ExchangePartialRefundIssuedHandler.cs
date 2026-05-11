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
        if (aggregate.FinalRefundAmount is not null) return; // already applied

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
