namespace Inventory.Management;

/// <summary>
/// Domain event: stock has been deducted from the source warehouse for a transfer.
/// Appended to the source ProductInventory stream (not the InventoryTransfer stream).
/// </summary>
public sealed record StockTransferredOut(
    string Sku,
    string WarehouseId,
    Guid TransferId,
    int Quantity,
    DateTimeOffset TransferredAt);
