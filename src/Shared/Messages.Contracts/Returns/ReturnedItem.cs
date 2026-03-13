namespace Messages.Contracts.Returns;

/// <summary>
/// Per-item disposition data in a completed or rejected return.
/// Inventory BC uses this to determine restocking; Orders BC uses it to verify refund line items.
/// Known RestockCondition values: "New", "LikeNew", "Opened".
/// </summary>
public sealed record ReturnedItem(
    string Sku,
    int Quantity,
    bool IsRestockable,
    string? WarehouseId,
    string? RestockCondition,
    decimal? RefundAmount,
    string? RejectionReason);
