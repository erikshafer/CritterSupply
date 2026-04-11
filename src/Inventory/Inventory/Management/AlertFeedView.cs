namespace Inventory.Management;

/// <summary>
/// Read model for the operations dashboard alert feed.
/// One row per alert — discrepancies, low-stock breaches, and replenishment triggers.
/// Async projection: not on the critical checkout path; tolerable staleness ≤ seconds.
/// </summary>
public sealed class AlertFeedView
{
    /// <summary>
    /// Auto-generated GUID identity for each alert entry.
    /// </summary>
    public Guid Id { get; set; }

    public string Sku { get; set; } = string.Empty;
    public string WarehouseId { get; set; } = string.Empty;
    public string AlertType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTimeOffset DetectedAt { get; set; }

    /// <summary>
    /// Optional severity level (Info, Warning, Critical).
    /// </summary>
    public string Severity { get; set; } = "Warning";

    /// <summary>
    /// Whether the alert has been acknowledged by an operator.
    /// </summary>
    public bool Acknowledged { get; set; }
}
