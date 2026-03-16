namespace Backoffice.Projections;

/// <summary>
/// BFF-owned projection for operations alert feed.
/// Each alert is a separate document keyed by alert ID (Guid).
/// Sourced from Inventory, Fulfillment, Payments, and Returns integration messages via RabbitMQ.
/// </summary>
public sealed record AlertFeedView
{
    /// <summary>
    /// Document ID: Unique alert identifier (stream ID from event store).
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Type of alert (LowStock, DeliveryFailed, PaymentFailed, ReturnExpired).
    /// </summary>
    public AlertType AlertType { get; init; }

    /// <summary>
    /// Severity level for filtering and prioritization.
    /// </summary>
    public AlertSeverity Severity { get; init; }

    /// <summary>
    /// When the alert was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Associated order ID (null for low stock alerts).
    /// </summary>
    public Guid? OrderId { get; init; }

    /// <summary>
    /// Human-readable alert message.
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Additional context data (SKU, warehouse ID, shipment ID, return ID, etc.).
    /// Serialized as JSON for flexibility.
    /// </summary>
    public string? ContextData { get; init; }

    /// <summary>
    /// Admin user who acknowledged this alert (null if not acknowledged).
    /// </summary>
    public Guid? AcknowledgedBy { get; init; }

    /// <summary>
    /// When the alert was acknowledged (null if not acknowledged).
    /// </summary>
    public DateTimeOffset? AcknowledgedAt { get; init; }
}

/// <summary>
/// Alert type classification for alert feed.
/// </summary>
public enum AlertType
{
    LowStock,
    DeliveryFailed,
    PaymentFailed,
    ReturnExpired
}

/// <summary>
/// Alert severity for filtering and prioritization.
/// </summary>
public enum AlertSeverity
{
    Info,
    Warning,
    Critical
}
