namespace Fulfillment.Shipments;

/// <summary>
/// Domain event when the routing engine assigns a fulfillment center to a shipment.
/// </summary>
public sealed record FulfillmentCenterAssigned(
    string FulfillmentCenterId,
    DateTimeOffset AssignedAt);

/// <summary>Domain event when a shipping label is generated.</summary>
public sealed record ShippingLabelGenerated(
    string Carrier,
    string Service,
    decimal BillableWeightLbs,
    string? LabelZpl,
    DateTimeOffset GeneratedAt);

/// <summary>Domain event when a tracking number is assigned to the shipment.</summary>
public sealed record TrackingNumberAssigned(
    string TrackingNumber,
    string Carrier,
    DateTimeOffset AssignedAt);

/// <summary>Domain event when the shipment is manifested for carrier pickup.</summary>
public sealed record ShipmentManifested(
    string ManifestId,
    DateTimeOffset ManifestTime);

/// <summary>Domain event when a package is moved to the carrier staging lane.</summary>
public sealed record PackageStagedForPickup(
    string StagingLane,
    string PickupWindow,
    DateTimeOffset StagedAt);

/// <summary>Domain event when the carrier driver scans the manifest at the FC.</summary>
public sealed record CarrierPickupConfirmed(
    string Carrier,
    bool DriverScan,
    DateTimeOffset PickupTime);

/// <summary>Domain event when physical custody is transferred to the carrier.</summary>
public sealed record ShipmentHandedToCarrier(
    string Carrier,
    string TrackingNumber,
    DateTimeOffset HandedAt);

/// <summary>Domain event for a carrier facility scan during transit.</summary>
public sealed record ShipmentInTransit(
    string FacilityScan,
    string ScanLocation,
    DateTimeOffset ScanTime);

/// <summary>Domain event for the carrier's last-mile out-for-delivery scan.</summary>
public sealed record OutForDelivery(
    string? LocalCarrier,
    DateTimeOffset? EstimatedDelivery,
    DateTimeOffset ScannedAt);

/// <summary>Domain event when a single delivery attempt fails.</summary>
public sealed record DeliveryAttemptFailed(
    int AttemptNumber,
    string ExceptionCode,
    string ExceptionDescription,
    DateTimeOffset AttemptDate);

/// <summary>Domain event when the carrier initiates return-to-sender after exhausting delivery attempts.</summary>
public sealed record ReturnToSenderInitiated(
    string Carrier,
    int TotalAttempts,
    int EstimatedReturnDays,
    DateTimeOffset InitiatedAt);

/// <summary>Domain event when a returned package is received back at the warehouse.</summary>
public sealed record ReturnReceivedAtWarehouse(
    DateTimeOffset ReceivedAt,
    string WarehouseId);
