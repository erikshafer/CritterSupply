using Marten;

namespace Inventory.Management;

/// <summary>
/// Wolverine handler for InitializeInventory commands.
/// Creates initial inventory tracking for a SKU at a warehouse.
/// </summary>
public static class InitializeInventoryHandler
{
    /// <summary>
    /// Handles an InitializeInventory command by creating new inventory aggregate.
    /// </summary>
    public static void Handle(
        InitializeInventory command,
        IDocumentSession session)
    {
        // Create inventory aggregate (pure function)
        var inventory = ProductInventory.Create(
            command.SKU,
            command.WarehouseId,
            command.InitialQuantity);

        // Persist events to Marten event store
        session.Events.StartStream<ProductInventory>(inventory.Id, inventory.PendingEvents.ToArray());
    }
}
