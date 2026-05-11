using Microsoft.Extensions.Logging;
using PaymentsMessages = Messages.Contracts.Payments;

namespace Returns.Integration;

/// <summary>
/// Stub consumer for <see cref="PaymentsMessages.ExchangeDeltaCaptureFailed"/>
/// (M47.0 / Slice 2). Slice 4 will turn this into the
/// "exchange cancelled" customer-visible path per the pending Gherkin
/// scenario "Additional payment capture fails — exchange cancelled" in
/// <c>docs/features/returns/cross-product-exchange.feature</c>.
///
/// <para>
/// In the interim, the handler emits a structured warning so a stranded
/// exchange (replacement reservation held by Inventory + customer
/// notified of an upcharge that never landed) is discoverable in
/// operational logs and dashboards. Per the UX Engineer's M47.0 / Slice 2
/// review, silent no-op was rejected as a UX-acceptable interim state.
/// </para>
///
/// <para>
/// This is intentionally NOT a no-op acknowledger like the M45.1 Orders
/// saga acknowledgers — those existed to suppress Wolverine "no handler"
/// noise on contracts that had no real consumer. Here the consumer
/// exists; what's deferred is the customer-facing transition.
/// </para>
/// </summary>
public static class ExchangeDeltaCaptureFailedHandler
{
    public static Task Handle(
        PaymentsMessages.ExchangeDeltaCaptureFailed message,
        ILogger<ExchangeDeltaCaptureFailedLog> logger,
        CancellationToken ct)
    {
        // Structured fields kept first-class so log aggregators / dashboards
        // can pivot on them. ReturnId + OrderId are the natural keys an
        // operator needs to manually unblock the exchange in Slice 4's
        // absence (deny the return + release the inventory hold).
        logger.LogWarning(
            "Cross-product exchange delta capture failed for ReturnId={ReturnId} OrderId={OrderId} " +
            "AmountDue={AmountDue} {Currency} Reason={Reason} IsRetriable={IsRetriable}. " +
            "Customer-visible cancellation is deferred to M47.0 / Slice 4 — manual ops " +
            "intervention required to deny the exchange and release the inventory reservation.",
            message.ReturnId,
            message.OrderId,
            message.AmountDue,
            message.Currency,
            message.Reason,
            message.IsRetriable);

        return Task.CompletedTask;
    }
}

/// <summary>
/// Marker type for the structured-log category. Keeps the log lines
/// pivotable on a stable category name independent of the static handler
/// class.
/// </summary>
public sealed class ExchangeDeltaCaptureFailedLog;
