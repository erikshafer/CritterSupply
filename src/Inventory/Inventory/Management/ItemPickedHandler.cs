using Marten;
using Wolverine;

namespace Inventory.Management;

/// <summary>
/// Handles ItemPicked integration messages from Fulfillment BC.
/// When a warehouse picker physically removes items from a bin, Inventory transitions
/// the allocation from Committed → Picked state.
///
/// Inline short pick detection (Slice 15): If picked quantity is less than committed,
/// a StockDiscrepancyFound event is appended via session.Events.Append() — this cannot
/// be expressed as a cascade per Anti-Pattern #13 (wolverine-message-handlers.md).
/// </summary>
public static class ItemPickedHandler
{
    public static async Task<ProductInventory?> Load(
        Messages.Contracts.Fulfillment.ItemPicked message,
        IDocumentSession session,
        CancellationToken ct)
    {
        var id = InventoryStreamId.Compute(message.Sku, message.WarehouseId);
        return await session.LoadAsync<ProductInventory>(id, ct);
    }

    public static OutgoingMessages Handle(
        Messages.Contracts.Fulfillment.ItemPicked message,
        ProductInventory? inventory,
        IDocumentSession session)
    {
        var outgoing = new OutgoingMessages();

        if (inventory is null)
            return outgoing; // no-op: unknown SKU at this warehouse

        // Find the committed allocation for this order
        var reservationId = inventory.ReservationOrderIds
            .FirstOrDefault(x => x.Value == message.OrderId).Key;

        if (reservationId == Guid.Empty || !inventory.CommittedAllocations.ContainsKey(reservationId))
        {
            // Stale message — reservation already released or committed to a different warehouse
            return outgoing;
        }

        var committedQty = inventory.CommittedAllocations[reservationId];

        if (message.Quantity == 0)
        {
            // Zero pick — complete bin miss. No StockPicked appended (nothing was physically removed).
            var discrepancy = new StockDiscrepancyFound(
                inventory.Sku, inventory.WarehouseId,
                committedQty, 0,
                DiscrepancyType.ZeroPick,
                "Complete bin miss — zero items found",
                DateTimeOffset.UtcNow);

            session.Events.Append(inventory.Id, discrepancy);

            outgoing.Add(new Messages.Contracts.Inventory.StockDiscrepancyDetected(
                discrepancy.Sku, discrepancy.WarehouseId,
                discrepancy.ExpectedQuantity, discrepancy.ActualQuantity,
                discrepancy.DiscrepancyType.ToString(), discrepancy.Description,
                discrepancy.DetectedAt));

            return outgoing;
        }

        // Append StockPicked (items physically removed from bin)
        session.Events.Append(inventory.Id,
            new StockPicked(
                inventory.Sku, inventory.WarehouseId,
                reservationId, message.Quantity,
                DateTimeOffset.UtcNow));

        // Short pick detection — picker found fewer items than committed
        if (message.Quantity < committedQty)
        {
            var discrepancy = new StockDiscrepancyFound(
                inventory.Sku, inventory.WarehouseId,
                committedQty, message.Quantity,
                DiscrepancyType.ShortPick,
                "Short pick detected during order fulfillment",
                DateTimeOffset.UtcNow);

            session.Events.Append(inventory.Id, discrepancy);

            outgoing.Add(new Messages.Contracts.Inventory.StockDiscrepancyDetected(
                discrepancy.Sku, discrepancy.WarehouseId,
                discrepancy.ExpectedQuantity, discrepancy.ActualQuantity,
                discrepancy.DiscrepancyType.ToString(), discrepancy.Description,
                discrepancy.DetectedAt));
        }

        return outgoing;
    }
}
