namespace Inventory.Api.Commands;

/// <summary>
/// Response DTO for inbound stock receipt operations.
/// </summary>
public sealed record ReceiveInboundStockResult(
    Guid InventoryId,
    string Sku,
    string WarehouseId,
    int NewAvailableQuantity);
