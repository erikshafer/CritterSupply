namespace Inventory.Management;

/// <summary>
/// Domain event indicating stock has been permanently written off (regulatory recall, disposal, etc.).
/// The actual quantity reduction is carried by a companion InventoryAdjusted event.
/// </summary>
public sealed record StockWrittenOff(
    string Sku,
    string WarehouseId,
    int Quantity,
    string Reason,
    string WrittenOffBy,
    DateTimeOffset WrittenOffAt);
