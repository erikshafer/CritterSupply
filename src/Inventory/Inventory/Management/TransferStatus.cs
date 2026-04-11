namespace Inventory.Management;

/// <summary>
/// Status enum for the <see cref="InventoryTransfer"/> aggregate lifecycle.
/// </summary>
public enum TransferStatus
{
    Requested,
    Shipped,
    Received,
    Cancelled
}
