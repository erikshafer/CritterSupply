namespace Inventory.Management;

/// <summary>
/// Domain event indicating physical damage has been recorded for inventory.
/// The actual quantity reduction is carried by a companion InventoryAdjusted event.
/// </summary>
public sealed record DamageRecorded(
    string Sku,
    string WarehouseId,
    int Quantity,
    string DamageReason,
    string RecordedBy,
    DateTimeOffset RecordedAt);
