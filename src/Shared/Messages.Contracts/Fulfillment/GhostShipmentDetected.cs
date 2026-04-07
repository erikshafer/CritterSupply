namespace Messages.Contracts.Fulfillment;

/// <summary>
/// Integration message from Fulfillment to Backoffice BC.
/// Published when a ghost shipment is detected (no carrier scan 24h after handoff).
/// Operations teams should investigate immediately.
/// </summary>
public sealed record GhostShipmentDetected(
    Guid OrderId,
    Guid ShipmentId,
    string TrackingNumber,
    TimeSpan TimeSinceHandoff,
    DateTimeOffset DetectedAt);
