namespace Messages.Contracts.Inventory;

/// <summary>
/// Reply published by the Inventory BC when a cross-product exchange
/// replacement reservation cannot be satisfied (no inventory record for
/// the SKU+warehouse, or insufficient available stock). Consumed by the
/// Returns BC, which on receipt transitions the corresponding return to
/// <c>Denied</c> with reason <c>"Replacement out of stock"</c> when the
/// return is still in the <c>Approved</c> state.
/// </summary>
public sealed record ReplacementReservationFailed(
    Guid ReturnId,
    Guid OrderId,
    string Sku,
    string WarehouseId,
    int RequestedQuantity,
    int AvailableQuantity,
    string Reason,
    DateTimeOffset FailedAt);
