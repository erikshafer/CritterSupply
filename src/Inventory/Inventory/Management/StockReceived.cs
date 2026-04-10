namespace Inventory.Management;

/// <summary>
/// Domain event indicating new stock has arrived from a supplier.
/// </summary>
public sealed record StockReceived(
    string Sku,
    string WarehouseId,
    string SupplierId,
    string? PurchaseOrderId,
    int Quantity,
    DateTimeOffset ReceivedAt);
