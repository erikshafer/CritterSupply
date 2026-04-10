namespace Messages.Contracts.Fulfillment;

/// <summary>
/// Integration message from Fulfillment to Inventory.
/// Published when an item is physically picked from a warehouse bin during order fulfillment.
/// Carries WarehouseId and OrderId for Inventory to correlate with committed allocations.
/// Gap #11 from event modeling retrospective: enriched with WarehouseId and OrderId.
/// </summary>
public sealed record ItemPicked(
    Guid OrderId,
    string Sku,
    string WarehouseId,
    int Quantity,
    DateTimeOffset PickedAt);
