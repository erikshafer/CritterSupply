namespace Inventory.Management;

/// <summary>
/// Domain event indicating all pending backorders for this SKU/warehouse have been cleared.
/// Sets HasPendingBackorders = false on the aggregate.
/// </summary>
public sealed record BackorderCleared(
    string Sku,
    string WarehouseId,
    DateTimeOffset ClearedAt);
