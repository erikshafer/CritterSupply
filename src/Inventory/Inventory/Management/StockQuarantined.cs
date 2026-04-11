namespace Inventory.Management;

/// <summary>
/// Domain event: stock has been placed in quarantine (suspect quality, pending investigation).
/// Appended alongside a negative InventoryAdjusted to decrement AvailableQuantity.
/// </summary>
public sealed record StockQuarantined(
    string Sku,
    string WarehouseId,
    Guid QuarantineId,
    int Quantity,
    string Reason,
    string QuarantinedBy,
    DateTimeOffset QuarantinedAt);
