namespace Inventory.Management;

/// <summary>
/// Domain event indicating a cycle count has been initiated for this SKU at this warehouse.
/// No state change on the aggregate — serves as an audit record.
/// </summary>
public sealed record CycleCountInitiated(
    string Sku,
    string WarehouseId,
    string InitiatedBy,
    DateTimeOffset InitiatedAt);
