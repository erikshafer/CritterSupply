namespace Inventory.Management;

/// <summary>
/// Domain event indicating stock has been physically picked from a bin.
/// Moves quantity from CommittedAllocations → PickedAllocations.
/// TotalOnHand is preserved (item is still in the building).
/// </summary>
public sealed record StockPicked(
    string Sku,
    string WarehouseId,
    Guid ReservationId,
    int Quantity,
    DateTimeOffset PickedAt);
