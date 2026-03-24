using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Messages.Contracts.Fulfillment;
using Messages.Contracts.Inventory;
using Messages.Contracts.Payments;
using Messages.Contracts.Returns;

namespace Backoffice.AlertManagement;

/// <summary>
/// Multi-stream projection aggregating alert events from multiple BCs.
/// Each integration message creates a separate AlertFeedView document.
/// Inline lifecycle ensures zero-lag visibility for operations dashboard.
/// </summary>
public sealed class AlertFeedViewProjection : MultiStreamProjection<AlertFeedView, Guid>
{
    public AlertFeedViewProjection()
    {
        // Map each integration message type to its own document (stream ID)
        Identity<LowStockDetected>(x => Guid.NewGuid()); // Generate new ID for each low stock alert
        Identity<ShipmentDeliveryFailed>(x => Guid.NewGuid()); // Generate new ID for each delivery failure
        Identity<PaymentFailed>(x => Guid.NewGuid()); // Generate new ID for each payment failure
        Identity<ReturnExpired>(x => Guid.NewGuid()); // Generate new ID for each return expiration
    }

    /// <summary>
    /// Create alert from LowStockDetected integration message.
    /// Severity: Warning (stock below threshold but not zero).
    /// </summary>
    public AlertFeedView Create(LowStockDetected evt)
    {
        return new AlertFeedView
        {
            Id = Guid.NewGuid(), // Stream ID will be set by Marten
            AlertType = AlertType.LowStock,
            Severity = evt.CurrentQuantity == 0 ? AlertSeverity.Critical : AlertSeverity.Warning,
            CreatedAt = evt.DetectedAt,
            OrderId = null, // Low stock alerts not order-specific
            Message = $"Low stock detected: {evt.Sku} at warehouse {evt.WarehouseId} ({evt.CurrentQuantity}/{evt.ThresholdQuantity})",
            ContextData = System.Text.Json.JsonSerializer.Serialize(new
            {
                evt.Sku,
                evt.WarehouseId,
                evt.CurrentQuantity,
                evt.ThresholdQuantity
            })
        };
    }

    /// <summary>
    /// Create alert from ShipmentDeliveryFailed integration message.
    /// Severity: Critical (customer impact, requires immediate attention).
    /// </summary>
    public AlertFeedView Create(ShipmentDeliveryFailed evt)
    {
        return new AlertFeedView
        {
            Id = Guid.NewGuid(),
            AlertType = AlertType.DeliveryFailed,
            Severity = AlertSeverity.Critical,
            CreatedAt = evt.FailedAt,
            OrderId = evt.OrderId,
            Message = $"Delivery failed for order {evt.OrderId}: {evt.Reason}",
            ContextData = System.Text.Json.JsonSerializer.Serialize(new
            {
                evt.OrderId,
                evt.ShipmentId,
                evt.Reason
            })
        };
    }

    /// <summary>
    /// Create alert from PaymentFailed integration message.
    /// Severity: Warning (payment retries may succeed) or Critical (non-retriable).
    /// </summary>
    public AlertFeedView Create(PaymentFailed evt)
    {
        return new AlertFeedView
        {
            Id = Guid.NewGuid(),
            AlertType = AlertType.PaymentFailed,
            Severity = evt.IsRetriable ? AlertSeverity.Warning : AlertSeverity.Critical,
            CreatedAt = evt.FailedAt,
            OrderId = evt.OrderId,
            Message = $"Payment failed for order {evt.OrderId}: {evt.FailureReason} (retriable: {evt.IsRetriable})",
            ContextData = System.Text.Json.JsonSerializer.Serialize(new
            {
                evt.OrderId,
                evt.PaymentId,
                evt.FailureReason,
                evt.IsRetriable
            })
        };
    }

    /// <summary>
    /// Create alert from ReturnExpired integration message.
    /// Severity: Info (customer missed return window, no immediate action needed).
    /// </summary>
    public AlertFeedView Create(ReturnExpired evt)
    {
        return new AlertFeedView
        {
            Id = Guid.NewGuid(),
            AlertType = AlertType.ReturnExpired,
            Severity = AlertSeverity.Info,
            CreatedAt = evt.ExpiredAt,
            OrderId = evt.OrderId,
            Message = $"Return expired for order {evt.OrderId} (customer ID: {evt.CustomerId})",
            ContextData = System.Text.Json.JsonSerializer.Serialize(new
            {
                evt.ReturnId,
                evt.OrderId,
                evt.CustomerId
            })
        };
    }
}
