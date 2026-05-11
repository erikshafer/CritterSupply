using Marten;
using Wolverine;
using IntegrationMessages = Messages.Contracts.Inventory;

namespace Inventory.Management;

/// <summary>
/// M47.0 / Slice 4 — handles
/// <see cref="IntegrationMessages.ReleaseExchangeReservation"/> integration
/// messages from the Returns BC. Releases a held replacement-stock
/// reservation that was previously placed by
/// <see cref="ReserveReplacementForExchangeHandler"/> when a cross-product
/// exchange is cancelled (payment-capture failure) or rejected (inspection
/// failure with captured delta).
///
/// <para>
/// Mirrors <see cref="ReleaseReservationHandler"/> in shape but consumes
/// the integration contract directly (no HTTP) and is fully idempotent
/// against missing reservations: when the reservation is no longer
/// present (already released, or expired by the ExpireReservation timer),
/// the handler is a silent no-op rather than a 404. This is intentional
/// because the Returns BC's compensation path may race against the
/// expiry timer.
/// </para>
///
/// <para>
/// Publishes the existing <see cref="IntegrationMessages.ReservationReleased"/>
/// integration event on success so downstream consumers (Backoffice
/// dashboards) see the same shape as a regular order-side release.
/// </para>
/// </summary>
public static class ReleaseExchangeReservationHandler
{
    public static async Task<ProductInventory?> Load(
        IntegrationMessages.ReleaseExchangeReservation message,
        IDocumentSession session,
        CancellationToken ct)
    {
        return await session.LoadAsync<ProductInventory>(message.InventoryId, ct);
    }

    public static OutgoingMessages Handle(
        IntegrationMessages.ReleaseExchangeReservation message,
        ProductInventory? inventory,
        IDocumentSession session)
    {
        var outgoing = new OutgoingMessages();

        // Idempotency / late-arrival guard. Either the inventory record
        // was deleted or the reservation has already been cleared by a
        // prior release / the ExpireReservation timer. No-op silently;
        // the Returns BC's compensation path is allowed to race.
        if (inventory is null) return outgoing;
        if (!inventory.Reservations.TryGetValue(message.ReservationId, out var quantity)) return outgoing;
        // Defensive: if the two reservation dictionaries are out of sync (should
        // never happen — both are populated together — but keeps the handler
        // crash-free and the message replayable rather than poisoned).
        if (!inventory.ReservationOrderIds.TryGetValue(message.ReservationId, out var orderId)) return outgoing;

        var releasedAt = DateTimeOffset.UtcNow;

        var domainEvent = new ReservationReleased(
            message.ReservationId,
            inventory.Sku,
            inventory.WarehouseId,
            quantity,
            message.Reason,
            releasedAt);

        session.Events.Append(inventory.Id, domainEvent);

        outgoing.Add(new IntegrationMessages.ReservationReleased(
            orderId,
            inventory.Id,
            message.ReservationId,
            inventory.Sku,
            inventory.WarehouseId,
            quantity,
            message.Reason,
            releasedAt));

        return outgoing;
    }
}
