using Marten;
using Returns.ReturnProcessing;
using Wolverine;
using PaymentsMessages = Messages.Contracts.Payments;
using ReturnsMessages = Messages.Contracts.Returns;

namespace Returns.Integration;

/// <summary>
/// Consumes the Payments BC's success reply for a cross-product exchange
/// price-difference delta capture. See ADR 0062 and
/// <c>docs/planning/milestones/m47-0-plan.md</c>.
///
/// <para>
/// On <see cref="PaymentsMessages.ExchangeDeltaCaptured"/>, this handler
/// appends the <see cref="ExchangeAdditionalPaymentCaptured"/> domain
/// event to the Return stream and republishes the public
/// <see cref="ReturnsMessages.ExchangeAdditionalPaymentCaptured"/> for
/// the Storefront / Orders / Backoffice fan-out (already routed in
/// <c>Returns.Api/Program.cs</c>).
/// </para>
///
/// <para>
/// Idempotency: only appends + publishes when the aggregate is a
/// cross-product exchange that has not yet recorded the captured
/// payment. Re-deliveries (or a late arrival after a manual override)
/// are no-ops.
/// </para>
/// </summary>
public static class ExchangeDeltaCapturedHandler
{
    public static async Task Handle(
        PaymentsMessages.ExchangeDeltaCaptured message,
        IDocumentSession session,
        IMessageBus bus,
        CancellationToken ct)
    {
        var stream = await session.Events.FetchForWriting<Return>(message.ReturnId, ct);
        var aggregate = stream.Aggregate;

        if (aggregate is null) return;
        if (!aggregate.IsCrossProductExchange) return;
        if (aggregate.AdditionalPaymentCaptured) return; // already applied

        var domainEvent = new ExchangeAdditionalPaymentCaptured(
            ReturnId: message.ReturnId,
            AmountCaptured: message.AmountCaptured,
            PaymentReference: message.TransactionId,
            CapturedAt: message.CapturedAt);

        stream.AppendOne(domainEvent);

        await bus.PublishAsync(new ReturnsMessages.ExchangeAdditionalPaymentCaptured(
            ReturnId: message.ReturnId,
            OrderId: aggregate.OrderId,
            CustomerId: aggregate.CustomerId,
            PaymentId: message.PaymentId,
            AmountCaptured: message.AmountCaptured,
            Currency: message.Currency,
            PaymentReference: message.TransactionId,
            CapturedAt: message.CapturedAt));
    }
}
