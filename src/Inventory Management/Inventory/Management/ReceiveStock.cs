namespace Inventory.Management;

/// <summary>
/// Command from warehouse system indicating new stock has arrived.
/// </summary>
public sealed record ReceiveStock(
    Guid InventoryId,
    int Quantity,
    string Source);
