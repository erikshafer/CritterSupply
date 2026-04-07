namespace Backoffice.DashboardReporting;

/// <summary>
/// Active fulfillment pipeline metrics for the Backoffice dashboard.
/// Singleton document (ID: "current") tracking shipments in transit, delivered, failed,
/// backordered, and lost. Updated via inline projection from Fulfillment BC integration messages.
/// </summary>
public sealed record FulfillmentPipelineView
{
    public string Id { get; init; } = "current";

    /// <summary>
    /// Count of shipments currently in transit (handed to carrier but not yet delivered or failed).
    /// </summary>
    public int ShipmentsInTransit { get; init; }

    /// <summary>
    /// Count of shipments successfully delivered.
    /// </summary>
    public int ShipmentsDelivered { get; init; }

    /// <summary>
    /// Count of shipments with delivery failures (return-to-sender initiated).
    /// </summary>
    public int DeliveryFailures { get; init; }

    /// <summary>
    /// Count of orders currently backordered (waiting on stock replenishment).
    /// </summary>
    public int Backorders { get; init; }

    /// <summary>
    /// Count of shipments lost in transit (no carrier scan for 5+ business days).
    /// </summary>
    public int ShipmentsLostInTransit { get; init; }

    /// <summary>
    /// Timestamp of the last projection update.
    /// </summary>
    public DateTimeOffset LastUpdatedAt { get; init; }
}
