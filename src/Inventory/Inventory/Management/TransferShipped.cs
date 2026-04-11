namespace Inventory.Management;

/// <summary>
/// Domain event: a transfer has been physically shipped from the source warehouse.
/// Appended to the InventoryTransfer stream.
/// </summary>
public sealed record TransferShipped(
    Guid TransferId,
    string ShippedBy,
    DateTimeOffset ShippedAt);
