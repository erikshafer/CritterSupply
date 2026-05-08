namespace Messages.Contracts.Inventory;

/// <summary>
/// Reply published by the Inventory BC when a cross-product exchange
/// replacement reservation succeeds. Consumed by the Returns BC.
/// Mirrors the field shape of <see cref="ReservationConfirmed"/> with
/// <see cref="ReturnId"/> in place of <c>ReservationId</c> for clarity at
/// the call site, even though they carry the same Guid value.
/// </summary>
public sealed record ReplacementReserved(
    Guid ReturnId,
    Guid OrderId,
    Guid InventoryId,
    string Sku,
    string WarehouseId,
    int Quantity,
    DateTimeOffset ReservedAt);
