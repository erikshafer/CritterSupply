using Marten;
using Marten.Linq.MatchesSql;
using Wolverine;

namespace Inventory.Management;

/// <summary>
/// Handles ShipmentHandedToCarrier integration messages from Fulfillment BC.
/// When a shipment is physically handed to the carrier, Inventory transitions
/// the allocation from Picked → Shipped, decrementing TotalOnHand.
///
/// Out-of-order delivery: If ShipmentHandedToCarrier arrives before ItemPicked
/// (common for small packages), the handler treats it as a combined pick-and-ship,
/// appending both StockPicked and StockShipped atomically.
/// </summary>
public static class ShipmentHandedToCarrierHandler
{
    public static async Task<ProductInventory?> Load(
        Messages.Contracts.Fulfillment.ShipmentHandedToCarrier message,
        IDocumentSession session,
        CancellationToken ct)
    {
        // Marten LINQ does not support Dictionary.Values.Contains().
        // Use MatchesSql with a JSONB existence check to find the ProductInventory
        // whose ReservationOrderIds dictionary contains the OrderId as a value.
        var orderIdText = message.OrderId.ToString();
        return await session.Query<ProductInventory>()
            .Where(i => i.MatchesSql(
                "EXISTS (SELECT 1 FROM jsonb_each_text(data->'ReservationOrderIds') kv WHERE kv.value = ?)",
                orderIdText))
            .FirstOrDefaultAsync(ct);
    }

    public static OutgoingMessages Handle(
        Messages.Contracts.Fulfillment.ShipmentHandedToCarrier message,
        ProductInventory? inventory,
        IDocumentSession session)
    {
        var outgoing = new OutgoingMessages();

        if (inventory is null)
            return outgoing; // no-op: no matching inventory for this order

        // Find the reservation for this order
        var reservationId = inventory.ReservationOrderIds
            .FirstOrDefault(x => x.Value == message.OrderId).Key;

        if (reservationId == Guid.Empty)
            return outgoing; // no matching reservation found

        var now = DateTimeOffset.UtcNow;

        // Check if item is already in Picked state
        if (inventory.PickedAllocations.TryGetValue(reservationId, out var pickedQty))
        {
            // Normal path: Picked → Shipped
            session.Events.Append(inventory.Id,
                new StockShipped(
                    inventory.Sku, inventory.WarehouseId,
                    reservationId, pickedQty,
                    message.ShipmentId, now));
        }
        else if (inventory.CommittedAllocations.TryGetValue(reservationId, out var committedQty))
        {
            // Out-of-order: ShipmentHandedToCarrier arrived before ItemPicked.
            // Combined pick-and-ship path — append both events atomically.
            session.Events.Append(inventory.Id,
                new StockPicked(
                    inventory.Sku, inventory.WarehouseId,
                    reservationId, committedQty, now));

            session.Events.Append(inventory.Id,
                new StockShipped(
                    inventory.Sku, inventory.WarehouseId,
                    reservationId, committedQty,
                    message.ShipmentId, now));
        }
        else
        {
            // Already shipped or reservation released — no-op (idempotent)
            return outgoing;
        }

        return outgoing;
    }
}
