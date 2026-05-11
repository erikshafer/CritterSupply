using Marten;
using Returns.ReturnProcessing;
using Wolverine;

namespace Returns.Integration;

/// <summary>
/// Consumes the Inventory BC's reply to a
/// <see cref="Messages.Contracts.Inventory.ReserveReplacementForExchange"/>
/// request. See ADR 0061 and
/// <c>docs/planning/milestones/m47-0-plan.md</c> for the slice plan.
///
/// <para>
/// On <see cref="Messages.Contracts.Inventory.ReplacementReserved"/>,
/// no aggregate state change is required for Slice 1 — the customer-visible
/// flow is unchanged and the existing Approved status covers the case.
/// (Slice 4 will release this reservation on rejection / cancellation.)
/// </para>
///
/// <para>
/// On <see cref="Messages.Contracts.Inventory.ReplacementReservationFailed"/>,
/// the exchange is denied with a customer-facing reason of
/// "Replacement out of stock". This handler is idempotent: it only
/// transitions the return when it is still in the
/// <see cref="ReturnStatus.Approved"/> state, so re-deliveries (or a
/// late-arriving failure after a manual override) are no-ops.
/// </para>
/// </summary>
public static class ReplacementReservationOutcomeHandler
{
    /// <summary>
    /// Customer-facing denial message used when the Inventory BC reports
    /// the replacement SKU could not be reserved. Mirrors the wording in
    /// <c>docs/features/returns/cross-product-exchange.feature</c>'s
    /// "Cross-product exchange denied — replacement out of stock"
    /// scenario so the Gherkin assertion has a single source of truth.
    /// </summary>
    public const string OutOfStockMessage =
        "Replacement item currently unavailable. Please request a refund or try again later.";

    /// <summary>
    /// Denial-reason discriminator used on the
    /// <see cref="ExchangeDenied"/> event when the cause is
    /// Inventory-reported unavailability of the replacement SKU.
    /// </summary>
    public const string OutOfStockReason = "ReplacementOutOfStock";

    public static async Task Handle(
        Messages.Contracts.Inventory.ReplacementReserved message,
        IDocumentSession session,
        CancellationToken ct)
    {
        // M47.0 / Slice 4 — capture the InventoryId on the Return aggregate
        // so subsequent compensation paths (cancellation on payment-capture
        // failure, rejection on inspection failure with captured delta) can
        // release the held stock without re-deriving the Inventory stream
        // id from inside the Returns BC.
        //
        // Idempotent: only appends the first time. At-least-once redelivery
        // re-finds the existing record and no-ops.
        var stream = await session.Events.FetchForWriting<Return>(message.ReturnId, ct);
        var aggregate = stream.Aggregate;

        if (aggregate is null) return;
        if (!aggregate.IsCrossProductExchange) return;
        if (aggregate.ReplacementInventoryId is not null) return;

        stream.AppendOne(new ReplacementReservationConfirmed(
            ReturnId: message.ReturnId,
            InventoryId: message.InventoryId,
            Sku: message.Sku,
            WarehouseId: message.WarehouseId,
            Quantity: message.Quantity,
            ReservedAt: message.ReservedAt));
    }

    public static async Task Handle(
        Messages.Contracts.Inventory.ReplacementReservationFailed message,
        IDocumentSession session,
        IMessageBus bus,
        CancellationToken ct)
    {
        var stream = await session.Events.FetchForWriting<Return>(message.ReturnId, ct);
        var aggregate = stream.Aggregate;

        // Idempotency / late-arrival guard. If the return doesn't exist
        // (lost message after stream deletion in tests) or has already
        // moved out of Approved (e.g. CS agent manually denied / a prior
        // failure was already applied / the exchange has been received
        // and inspected), a re-delivery must not corrupt state.
        if (aggregate is null) return;
        if (!aggregate.IsCrossProductExchange) return;
        if (aggregate.Status != ReturnStatus.Approved) return;

        var now = DateTimeOffset.UtcNow;

        var domainEvent = new ExchangeDenied(
            ReturnId: message.ReturnId,
            Reason: OutOfStockReason,
            Message: OutOfStockMessage,
            DeniedAt: now);

        stream.AppendOne(domainEvent);

        // Mirror the publication shape of DenyExchangeHandler so any
        // existing subscriber (Backoffice dashboards, Storefront UI) sees
        // the denial regardless of whether it originated from a CS agent
        // command or from this Inventory-driven path.
        await bus.PublishAsync(new Messages.Contracts.Returns.ExchangeDenied(
            ReturnId: message.ReturnId,
            OrderId: aggregate.OrderId,
            CustomerId: aggregate.CustomerId,
            Reason: OutOfStockReason,
            Message: OutOfStockMessage,
            DeniedAt: now));
    }
}
