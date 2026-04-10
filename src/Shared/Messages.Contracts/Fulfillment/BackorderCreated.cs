namespace Messages.Contracts.Fulfillment;

/// <summary>
/// Integration message from Fulfillment to Orders and Inventory.
/// Published when a shipment is backordered due to no stock availability.
/// Orders saga should notify the customer. Inventory registers backorder state.
/// Enriched with Items for SKU-level tracking (Gap #10 from event modeling retrospective).
/// </summary>
public sealed record BackorderCreated(
    Guid OrderId,
    Guid ShipmentId,
    string Reason,
    IReadOnlyList<BackorderedItem> Items,
    DateTimeOffset CreatedAt);

/// <summary>
/// Per-item detail within a BackorderCreated message.
/// </summary>
public sealed record BackorderedItem(
    string Sku,
    string WarehouseId,
    int Quantity);
