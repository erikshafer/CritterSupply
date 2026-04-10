namespace Inventory.Management;

/// <summary>
/// Domain event indicating a soft reservation has been converted to a hard allocation.
/// </summary>
public sealed record ReservationCommitted(
    Guid ReservationId,
    string Sku,
    string WarehouseId,
    DateTimeOffset CommittedAt);
