namespace Inventory.Management;

/// <summary>
/// Domain event: stock has been received at a warehouse from a transfer.
/// Appended to the destination ProductInventory stream (not the InventoryTransfer stream).
/// Mirrors StockReceived but with transfer-specific provenance.
/// </summary>
public sealed record StockTransferredIn(
    string Sku,
    string WarehouseId,
    Guid TransferId,
    int Quantity,
    DateTimeOffset ReceivedAt);
