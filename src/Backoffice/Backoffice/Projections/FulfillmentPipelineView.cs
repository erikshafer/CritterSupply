namespace Backoffice.Projections;

/// <summary>
/// Active fulfillment pipeline metrics for the Backoffice dashboard.
/// Singleton document (ID: "current") tracking shipments in transit, delivered, and failed.
/// Updated via inline projection from Fulfillment BC integration messages.
/// </summary>
public sealed record FulfillmentPipelineView
{
    public string Id { get; init; } = "current";

    /// <summary>
    /// Count of shipments currently in transit (dispatched but not yet delivered or failed).
    /// </summary>
    public int ShipmentsInTransit { get; init; }

    /// <summary>
    /// Count of shipments successfully delivered.
    /// </summary>
    public int ShipmentsDelivered { get; init; }

    /// <summary>
    /// Count of shipments with failed delivery attempts.
    /// </summary>
    public int DeliveryFailures { get; init; }

    /// <summary>
    /// Timestamp of the last projection update.
    /// </summary>
    public DateTimeOffset LastUpdatedAt { get; init; }
}
