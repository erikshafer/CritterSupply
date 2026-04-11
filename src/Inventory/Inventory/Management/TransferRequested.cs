namespace Inventory.Management;

/// <summary>
/// Domain event: an inter-warehouse transfer has been requested.
/// Appended to the InventoryTransfer stream.
/// </summary>
public sealed record TransferRequested(
    Guid TransferId,
    string Sku,
    string SourceWarehouseId,
    string DestinationWarehouseId,
    int Quantity,
    string RequestedBy,
    DateTimeOffset RequestedAt);
