using Marten;
using Wolverine;

namespace Inventory.Management;

/// <summary>
/// Handles BackorderCreated integration messages from Fulfillment BC.
/// For each backordered item, loads the corresponding ProductInventory stream
/// and appends BackorderRegistered, setting HasPendingBackorders = true.
/// </summary>
public static class BackorderCreatedHandler
{
    public static async Task Handle(
        Messages.Contracts.Fulfillment.BackorderCreated message,
        IDocumentSession session,
        CancellationToken ct)
    {
        if (message.Items is null || message.Items.Count == 0)
            return; // No items to register — legacy message without SKU data

        foreach (var item in message.Items)
        {
            var inventoryId = InventoryStreamId.Compute(item.Sku, item.WarehouseId);
            var inventory = await session.LoadAsync<ProductInventory>(inventoryId, ct);

            if (inventory is null)
                continue; // Unknown SKU at this warehouse — skip

            session.Events.Append(inventoryId,
                new BackorderRegistered(
                    item.Sku,
                    item.WarehouseId,
                    message.OrderId,
                    message.ShipmentId,
                    item.Quantity,
                    DateTimeOffset.UtcNow));
        }
    }
}
