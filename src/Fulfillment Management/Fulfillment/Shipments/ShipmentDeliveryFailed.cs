namespace Fulfillment.Shipments;

/// <summary>
/// Domain event when delivery attempt fails.
/// </summary>
public sealed record ShipmentDeliveryFailed(
    string Reason,
    DateTimeOffset FailedAt);
