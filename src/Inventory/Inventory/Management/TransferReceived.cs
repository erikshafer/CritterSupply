namespace Inventory.Management;

/// <summary>
/// Domain event: a transfer has been received at the destination warehouse (full quantity).
/// Appended to the InventoryTransfer stream.
/// </summary>
public sealed record TransferReceived(
    Guid TransferId,
    int ReceivedQuantity,
    string ReceivedBy,
    DateTimeOffset ReceivedAt);
