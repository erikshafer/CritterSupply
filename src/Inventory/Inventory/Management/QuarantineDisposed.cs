namespace Inventory.Management;

/// <summary>
/// Domain event: quarantined stock has been disposed (damaged, contaminated, or recalled).
/// Appended alongside StockWrittenOff — no resurrection possible.
/// </summary>
public sealed record QuarantineDisposed(
    string Sku,
    string WarehouseId,
    Guid QuarantineId,
    int Quantity,
    string DisposalReason,
    string DisposedBy,
    DateTimeOffset DisposedAt);
