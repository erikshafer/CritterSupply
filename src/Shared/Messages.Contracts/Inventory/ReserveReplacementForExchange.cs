namespace Messages.Contracts.Inventory;

/// <summary>
/// Request from the Returns BC to place a hold on the replacement SKU
/// for a cross-product exchange. Consumed by the Inventory BC's
/// <c>ReserveReplacementForExchangeHandler</c>, which reuses the standard
/// <c>ProductInventory</c> reservation lifecycle (see ADR 0061).
///
/// <para>
/// Idempotency: the receiving handler keys off <see cref="ReturnId"/>
/// (used as the underlying reservation id). Re-deliveries under
/// at-least-once semantics are no-ops once the reservation exists.
/// </para>
/// </summary>
public sealed record ReserveReplacementForExchange(
    Guid ReturnId,
    Guid OrderId,
    Guid CustomerId,
    string ReplacementSku,
    string WarehouseId,
    int Quantity,
    DateTimeOffset RequestedAt);
