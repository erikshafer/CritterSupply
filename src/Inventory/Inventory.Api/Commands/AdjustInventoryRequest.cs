namespace Inventory.Api.Commands;

/// <summary>
/// Request DTO for adjusting inventory quantities.
/// </summary>
public sealed record AdjustInventoryRequest(
    int AdjustmentQuantity,
    string Reason,
    string AdjustedBy);
