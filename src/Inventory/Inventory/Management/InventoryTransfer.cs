namespace Inventory.Management;

/// <summary>
/// Event-sourced aggregate representing an inter-warehouse stock transfer.
/// Stream IDs use Guid.CreateVersion7() — transfers are not SKU+Warehouse keyed.
/// Lifecycle: Requested → Shipped → Received (or Cancelled pre-ship).
/// </summary>
public sealed record InventoryTransfer(
    Guid Id,
    string Sku,
    string SourceWarehouseId,
    string DestinationWarehouseId,
    int Quantity,
    TransferStatus Status,
    int? ReceivedQuantity,
    string RequestedBy,
    DateTimeOffset RequestedAt)
{
    public static InventoryTransfer Create(TransferRequested @event) =>
        new(@event.TransferId,
            @event.Sku,
            @event.SourceWarehouseId,
            @event.DestinationWarehouseId,
            @event.Quantity,
            TransferStatus.Requested,
            null,
            @event.RequestedBy,
            @event.RequestedAt);

    public InventoryTransfer Apply(TransferShipped @event) =>
        this with { Status = TransferStatus.Shipped };

    public InventoryTransfer Apply(TransferReceived @event) =>
        this with
        {
            Status = TransferStatus.Received,
            ReceivedQuantity = @event.ReceivedQuantity
        };

    public InventoryTransfer Apply(TransferShortReceived @event) =>
        this with
        {
            Status = TransferStatus.Received,
            ReceivedQuantity = @event.ReceivedQuantity
        };

    public InventoryTransfer Apply(TransferCancelled @event) =>
        this with { Status = TransferStatus.Cancelled };
}
