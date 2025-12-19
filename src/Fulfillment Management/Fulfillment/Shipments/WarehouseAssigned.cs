namespace Fulfillment.Shipments;

/// <summary>
/// Domain event when a warehouse is assigned to fulfill the shipment.
/// </summary>
public sealed record WarehouseAssigned(
    string WarehouseId,
    DateTimeOffset AssignedAt);
