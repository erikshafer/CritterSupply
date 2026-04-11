namespace Inventory.Management;

/// <summary>
/// Domain event: a transfer has been cancelled before shipping.
/// Appended to the InventoryTransfer stream.
/// </summary>
public sealed record TransferCancelled(
    Guid TransferId,
    string Reason,
    string CancelledBy,
    DateTimeOffset CancelledAt);
