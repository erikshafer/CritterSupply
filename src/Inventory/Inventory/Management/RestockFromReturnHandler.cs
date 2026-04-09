using Marten;
using Messages.Contracts.Returns;
using Wolverine;
using IntegrationMessages = Messages.Contracts.Inventory;

namespace Inventory.Management;

/// <summary>
/// Handles ReturnCompleted integration messages from Returns BC.
/// Restocks eligible items that passed inspection back into inventory.
/// Only processes items where IsRestockable is true and WarehouseId is provided.
/// </summary>
public static class RestockFromReturnHandler
{
    public static async Task<OutgoingMessages> Handle(
        ReturnCompleted message,
        IDocumentSession session,
        CancellationToken ct)
    {
        var outgoing = new OutgoingMessages();
        var restockedAt = DateTimeOffset.UtcNow;

        foreach (var item in message.Items.Where(i => i.IsRestockable && i.WarehouseId is not null))
        {
            var inventoryId = InventoryStreamId.Compute(item.Sku, item.WarehouseId!);
            var inventory = await session.LoadAsync<ProductInventory>(inventoryId, ct);

            if (inventory is null)
                continue; // SKU not tracked at this warehouse — skip

            var domainEvent = new StockRestocked(
                item.Sku,
                item.WarehouseId!,
                message.ReturnId,
                item.Quantity,
                restockedAt);

            session.Events.Append(inventoryId, domainEvent);

            outgoing.Add(new IntegrationMessages.StockReplenished(
                item.Sku,
                item.WarehouseId!,
                item.Quantity,
                inventory.AvailableQuantity + item.Quantity,
                restockedAt));
        }

        return outgoing;
    }
}
