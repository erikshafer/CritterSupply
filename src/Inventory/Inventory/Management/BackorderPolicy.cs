using Wolverine;

namespace Inventory.Management;

/// <summary>
/// Inline policy for detecting when stock arrives for a backordered SKU.
/// Called from ReceiveStockHandler (after StockReceived is appended) and
/// RestockFromReturnHandler (after StockRestocked is appended).
///
/// Does NOT publish from ShipmentHandedToCarrierHandler — shipping decrements stock,
/// it does not increase availability.
/// </summary>
public static class BackorderPolicy
{
    /// <summary>
    /// Returns outgoing messages if backorder notification should be sent.
    /// Returns null if no action needed.
    /// </summary>
    public static OutgoingMessages? CheckAndPublish(
        ProductInventory inventory, int newAvailableQuantity)
    {
        if (!inventory.HasPendingBackorders)
            return null;

        if (newAvailableQuantity <= 0)
            return null;

        var outgoing = new OutgoingMessages();
        outgoing.Add(new Messages.Contracts.Inventory.BackorderStockAvailable(
            inventory.Sku,
            inventory.WarehouseId,
            newAvailableQuantity,
            DateTimeOffset.UtcNow));

        return outgoing;
    }
}
