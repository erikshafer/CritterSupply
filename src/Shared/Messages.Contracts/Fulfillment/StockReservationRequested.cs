namespace Messages.Contracts.Fulfillment;

/// <summary>
/// Integration message sent by Fulfillment BC to Inventory BC when the routing engine
/// has selected a warehouse for an order line item. Carries the routing-informed WarehouseId
/// instead of the hardcoded WH-01 from the legacy OrderPlacedHandler flow.
/// </summary>
public sealed record StockReservationRequested(
    Guid OrderId,
    string Sku,
    string WarehouseId,
    Guid ReservationId,
    int Quantity);
