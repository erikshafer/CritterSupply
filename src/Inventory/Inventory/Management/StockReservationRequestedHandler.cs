using Marten;
using Messages.Contracts.Fulfillment;
using Wolverine;
using IntegrationMessages = Messages.Contracts.Inventory;

namespace Inventory.Management;

/// <summary>
/// Handles StockReservationRequested integration messages from Fulfillment BC.
/// This is the routing-aware reservation flow that replaced the legacy
/// OrderPlacedHandler's hardcoded WH-01 path (M43.0 — Slice 12 retirement).
///
/// Business logic mirrors ReserveStockHandler, but the trigger is an
/// integration message (Fulfillment → Inventory) rather than an internal HTTP command.
/// On insufficient stock, this handler publishes <see cref="IntegrationMessages.ReservationFailed"/>
/// rather than returning a ProblemDetails response (HTTP semantics don't apply
/// to a queued integration message). The Orders saga consumes both the success
/// and failure outcomes to drive the order lifecycle.
/// </summary>
public static class StockReservationRequestedHandler
{
    public static async Task<ProductInventory?> Load(
        StockReservationRequested message,
        IDocumentSession session,
        CancellationToken ct)
    {
        var inventoryId = InventoryStreamId.Compute(message.Sku, message.WarehouseId);
        return await session.LoadAsync<ProductInventory>(inventoryId, ct);
    }

    public static OutgoingMessages Handle(
        StockReservationRequested message,
        ProductInventory? inventory,
        IDocumentSession session)
    {
        var now = DateTimeOffset.UtcNow;
        var outgoing = new OutgoingMessages();

        // Inventory not found — surface as a failure so the Orders saga can compensate
        // (rather than silently dropping). Orders treats this as out-of-stock equivalent.
        if (inventory is null)
        {
            outgoing.Add(new IntegrationMessages.ReservationFailed(
                message.OrderId,
                message.ReservationId,
                message.Sku,
                message.WarehouseId,
                message.Quantity,
                AvailableQuantity: 0,
                Reason: $"No inventory found for SKU {message.Sku} at warehouse {message.WarehouseId}",
                FailedAt: now));
            return outgoing;
        }

        // Idempotency guard: at-least-once delivery may redeliver the same
        // ReservationId. If we've already applied it, do nothing (no double-reserve,
        // no duplicate ReservationConfirmed, no duplicate expiry schedule).
        if (inventory.Reservations.ContainsKey(message.ReservationId))
            return outgoing;

        if (inventory.AvailableQuantity < message.Quantity)
        {
            outgoing.Add(new IntegrationMessages.ReservationFailed(
                message.OrderId,
                message.ReservationId,
                message.Sku,
                message.WarehouseId,
                message.Quantity,
                AvailableQuantity: inventory.AvailableQuantity,
                Reason: $"Insufficient stock for SKU {message.Sku}. Requested: {message.Quantity}, Available: {inventory.AvailableQuantity}",
                FailedAt: now));
            return outgoing;
        }

        var domainEvent = new StockReserved(
            message.OrderId,
            message.ReservationId,
            message.Sku,
            message.WarehouseId,
            message.Quantity,
            now);

        session.Events.Append(inventory.Id, domainEvent);

        outgoing.Add(new IntegrationMessages.ReservationConfirmed(
            message.OrderId,
            inventory.Id,
            message.ReservationId,
            message.Sku,
            message.WarehouseId,
            message.Quantity,
            now));

        // Schedule reservation expiry — if not committed within timeout, stock returns to pool
        outgoing.Delay(
            new ExpireReservation(message.ReservationId, inventory.Id),
            ExpireReservationHandler.ExpiryTimeout);

        return outgoing;
    }
}
