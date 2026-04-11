namespace Inventory.Management;

/// <summary>
/// Domain event: a transfer was received with less quantity than shipped.
/// Appended to the InventoryTransfer stream alongside a StockDiscrepancyFound
/// on the destination ProductInventory stream.
/// </summary>
public sealed record TransferShortReceived(
    Guid TransferId,
    int ShippedQuantity,
    int ReceivedQuantity,
    int ShortQuantity,
    string ReceivedBy,
    DateTimeOffset ReceivedAt);
