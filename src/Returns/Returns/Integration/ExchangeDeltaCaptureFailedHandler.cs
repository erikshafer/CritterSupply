using Marten;
using Microsoft.Extensions.Logging;
using Returns.ReturnProcessing;
using Wolverine;
using InventoryMessages = Messages.Contracts.Inventory;
using PaymentsMessages = Messages.Contracts.Payments;
using ReturnsMessages = Messages.Contracts.Returns;

namespace Returns.Integration;

/// <summary>
/// M47.0 / Slice 4 — consumer for
/// <see cref="PaymentsMessages.ExchangeDeltaCaptureFailed"/> that closes
/// the customer-facing cancellation path the M47.0 / Slice 2 stub
/// deferred. Implements the
/// "Additional payment capture fails — exchange cancelled" Gherkin scenario
/// in <c>docs/features/returns/cross-product-exchange.feature</c>.
///
/// <para>
/// Behaviour:
/// <list type="number">
///   <item>Loads the <see cref="Return"/> aggregate. If the aggregate is
///     missing, not a cross-product exchange, has already captured the
///     delta, or is no longer in the <see cref="ReturnStatus.Approved"/>
///     state, the handler is a no-op (idempotent under at-least-once).</item>
///   <item>Appends the <see cref="ExchangeCancelled"/> domain event,
///     transitioning the aggregate to <see cref="ReturnStatus.Cancelled"/>
///     (terminal).</item>
///   <item>Publishes the public
///     <see cref="ReturnsMessages.ExchangeCancelled"/> integration message
///     (Storefront / Orders / Backoffice fan-out).</item>
///   <item>If the Slice 1 replacement reservation was confirmed
///     (<see cref="Return.ReplacementInventoryId"/> non-null), publishes
///     <see cref="InventoryMessages.ReleaseReservation"/> to release the
///     held stock so it returns to the available pool immediately rather
///     than waiting for the ExpireReservation timer.</item>
/// </list>
/// </para>
///
/// <para>
/// The customer-facing copy is the verbatim Gherkin string. The Reason
/// field is a stable machine-pivotable code.
/// </para>
///
/// <para>
/// A structured warning log line is still emitted so operators have the
/// same dashboard-pivotable signal the Slice 2 stub provided.
/// </para>
/// </summary>
public static class ExchangeDeltaCaptureFailedHandler
{
    /// <summary>Stable machine-pivotable cancellation reason code.</summary>
    public const string CancellationReason = "PaymentCaptureFailed";

    /// <summary>Customer-facing copy from the Gherkin spec, verbatim.</summary>
    public const string CancellationMessage =
        "Payment for price difference could not be processed. Exchange cancelled.";

    public static async Task Handle(
        PaymentsMessages.ExchangeDeltaCaptureFailed message,
        IDocumentSession session,
        IMessageBus bus,
        ILogger<ExchangeDeltaCaptureFailedLog> logger,
        CancellationToken ct)
    {
        // Operational signal — preserved from the Slice 2 stub so dashboards
        // and log aggregators continue to pivot on the same fields.
        logger.LogWarning(
            "Cross-product exchange delta capture failed for ReturnId={ReturnId} OrderId={OrderId} " +
            "AmountDue={AmountDue} {Currency} Reason={Reason} IsRetriable={IsRetriable}.",
            message.ReturnId,
            message.OrderId,
            message.AmountDue,
            message.Currency,
            message.Reason,
            message.IsRetriable);

        var stream = await session.Events.FetchForWriting<Return>(message.ReturnId, ct);
        var aggregate = stream.Aggregate;

        // Idempotency / safety guards. Mirrors the Slice 2
        // ExchangeDeltaCapturedHandler shape: silently no-op on
        // redelivery or on an aggregate state where the cancellation
        // would be incorrect.
        if (aggregate is null) return;
        if (!aggregate.IsCrossProductExchange) return;
        if (aggregate.AdditionalPaymentCaptured) return; // capture succeeded — failure must be stale
        if (aggregate.Status != ReturnStatus.Approved) return; // already moved past Approved (cancelled / progressed elsewhere)

        var cancelledAt = message.FailedAt;

        var domainEvent = new ExchangeCancelled(
            ReturnId: message.ReturnId,
            Reason: CancellationReason,
            Message: CancellationMessage,
            CancelledAt: cancelledAt);

        stream.AppendOne(domainEvent);

        // Public fan-out — Storefront notifies the customer with the
        // verbatim Gherkin copy via the BuildReturnMessage("Cancelled", …)
        // mapper helper.
        await bus.PublishAsync(new ReturnsMessages.ExchangeCancelled(
            ReturnId: message.ReturnId,
            OrderId: aggregate.OrderId,
            CustomerId: aggregate.CustomerId,
            Reason: CancellationReason,
            Message: CancellationMessage,
            CancelledAt: cancelledAt));

        // Release the Slice 1 replacement-stock reservation. Skipped when
        // the InventoryId was never recorded (e.g. the
        // ReplacementReserved reply lost the race, or the test seeded the
        // aggregate without going through ApproveExchange — in that case
        // ExpireReservation will eventually clean up). ReturnId is the
        // ReservationId per ADR 0061.
        if (aggregate.ReplacementInventoryId is { } inventoryId)
        {
            await bus.PublishAsync(new InventoryMessages.ReleaseExchangeReservation(
                InventoryId: inventoryId,
                ReservationId: message.ReturnId,
                Reason: CancellationReason));
        }
    }
}

/// <summary>
/// Marker type for the structured-log category. Keeps the log lines
/// pivotable on a stable category name independent of the static handler
/// class.
/// </summary>
public sealed class ExchangeDeltaCaptureFailedLog;
