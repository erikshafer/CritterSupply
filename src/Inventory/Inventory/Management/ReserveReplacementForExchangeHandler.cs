using Marten;
using Wolverine;
using IntegrationMessages = Messages.Contracts.Inventory;

namespace Inventory.Management;

/// <summary>
/// Handles <see cref="IntegrationMessages.ReserveReplacementForExchange"/>
/// integration messages from the Returns BC. Reuses the standard
/// <see cref="ProductInventory"/> reservation lifecycle to place a hold
/// on the replacement SKU for a cross-product exchange. See
/// <c>docs/decisions/0061-cross-product-exchange-replacement-reservation.md</c>.
///
/// <para>
/// Mirrors <see cref="StockReservationRequestedHandler"/> in shape:
/// idempotent on the supplied <c>ReturnId</c> (used as the underlying
/// <see cref="StockReserved.ReservationId"/>), publishes a
/// <see cref="IntegrationMessages.ReplacementReservationFailed"/> reply
/// when the inventory record is missing or stock is insufficient, and
/// publishes <see cref="IntegrationMessages.ReplacementReserved"/> on
/// success along with scheduling the same expiry as a regular order
/// reservation.
/// </para>
/// </summary>
public static class ReserveReplacementForExchangeHandler
{
    public static async Task<ProductInventory?> Load(
        IntegrationMessages.ReserveReplacementForExchange message,
        IDocumentSession session,
        CancellationToken ct)
    {
        var inventoryId = InventoryStreamId.Compute(message.ReplacementSku, message.WarehouseId);
        return await session.LoadAsync<ProductInventory>(inventoryId, ct);
    }

    public static OutgoingMessages Handle(
        IntegrationMessages.ReserveReplacementForExchange message,
        ProductInventory? inventory,
        IDocumentSession session)
    {
        var now = DateTimeOffset.UtcNow;
        var outgoing = new OutgoingMessages();

        // Inventory not found — surface as a failure so Returns can
        // deny the exchange (rather than silently dropping). Returns
        // treats this as out-of-stock equivalent.
        if (inventory is null)
        {
            outgoing.Add(new IntegrationMessages.ReplacementReservationFailed(
                ReturnId: message.ReturnId,
                OrderId: message.OrderId,
                Sku: message.ReplacementSku,
                WarehouseId: message.WarehouseId,
                RequestedQuantity: message.Quantity,
                AvailableQuantity: 0,
                Reason: $"No inventory found for SKU {message.ReplacementSku} at warehouse {message.WarehouseId}",
                FailedAt: now));
            return outgoing;
        }

        // Idempotency guard: at-least-once delivery may redeliver the
        // same ReturnId. If we've already applied it, re-publish the
        // confirmation (so a Returns redelivery loss does not strand
        // the reservation) but do not double-reserve, double-schedule
        // expiry, or append a duplicate StockReserved.
        if (inventory.Reservations.ContainsKey(message.ReturnId))
        {
            outgoing.Add(new IntegrationMessages.ReplacementReserved(
                ReturnId: message.ReturnId,
                OrderId: message.OrderId,
                InventoryId: inventory.Id,
                Sku: message.ReplacementSku,
                WarehouseId: message.WarehouseId,
                Quantity: inventory.Reservations[message.ReturnId],
                ReservedAt: now));
            return outgoing;
        }

        if (inventory.AvailableQuantity < message.Quantity)
        {
            outgoing.Add(new IntegrationMessages.ReplacementReservationFailed(
                ReturnId: message.ReturnId,
                OrderId: message.OrderId,
                Sku: message.ReplacementSku,
                WarehouseId: message.WarehouseId,
                RequestedQuantity: message.Quantity,
                AvailableQuantity: inventory.AvailableQuantity,
                Reason: $"Insufficient stock for SKU {message.ReplacementSku}. Requested: {message.Quantity}, Available: {inventory.AvailableQuantity}",
                FailedAt: now));
            return outgoing;
        }

        // ReturnId is used as ReservationId per ADR 0061. OrderId on
        // the reservation record is the original Order's id (so the
        // existing ReservationOrderIds invariant is satisfied and any
        // operational tooling that traces reservations back to orders
        // still works).
        var domainEvent = new StockReserved(
            OrderId: message.OrderId,
            ReservationId: message.ReturnId,
            Sku: message.ReplacementSku,
            WarehouseId: message.WarehouseId,
            Quantity: message.Quantity,
            ReservedAt: now);

        session.Events.Append(inventory.Id, domainEvent);

        outgoing.Add(new IntegrationMessages.ReplacementReserved(
            ReturnId: message.ReturnId,
            OrderId: message.OrderId,
            InventoryId: inventory.Id,
            Sku: message.ReplacementSku,
            WarehouseId: message.WarehouseId,
            Quantity: message.Quantity,
            ReservedAt: now));

        // Schedule reservation expiry — same semantics as order
        // reservations. If the exchange does not progress to commit
        // (e.g. customer never ships the original, inspection fails)
        // the held stock returns to the available pool. Slice 4 will
        // add explicit release on rejection / cancellation paths.
        outgoing.Delay(
            new ExpireReservation(message.ReturnId, inventory.Id),
            ExpireReservationHandler.ExpiryTimeout);

        return outgoing;
    }
}
