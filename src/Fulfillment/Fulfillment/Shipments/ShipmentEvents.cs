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

// --- Failure Mode Events (P1) ---

/// <summary>Domain event when a shipment is rerouted to a different fulfillment center.</summary>
public sealed record ShipmentRerouted(
    string OriginalFulfillmentCenter,
    string NewFulfillmentCenter,
    DateTimeOffset ReroutedAt);

/// <summary>Domain event when a shipment is backordered due to no stock anywhere.</summary>
public sealed record BackorderCreated(
    string Reason,
    DateTimeOffset CreatedAt);

/// <summary>Domain event when shipping label generation fails.</summary>
public sealed record ShippingLabelGenerationFailed(
    string Carrier,
    string FailureReason,
    DateTimeOffset FailedAt);

/// <summary>Domain event when a carrier missed the scheduled pickup window.</summary>
public sealed record CarrierPickupMissed(
    string Carrier,
    string PickupWindow,
    DateTimeOffset DetectedAt);

/// <summary>Domain event when carrier relations are escalated due to missed pickup.</summary>
public sealed record CarrierRelationsEscalated(
    string Carrier,
    string Reason,
    DateTimeOffset EscalatedAt);

/// <summary>Domain event when an alternate carrier is arranged after a missed pickup.</summary>
public sealed record AlternateCarrierArranged(
    string OriginalCarrier,
    string NewCarrier,
    DateTimeOffset ArrangedAt);

/// <summary>Domain event when a shipping label is voided (e.g., carrier change).</summary>
public sealed record ShippingLabelVoided(
    string Carrier,
    string Reason,
    DateTimeOffset VoidedAt);

/// <summary>Domain event when a ghost shipment is detected (no scan 24h after handoff).</summary>
public sealed record GhostShipmentDetected(
    string TrackingNumber,
    TimeSpan TimeSinceHandoff,
    DateTimeOffset DetectedAt);

/// <summary>Domain event when a shipment is determined lost in transit.</summary>
public sealed record ShipmentLostInTransit(
    string Carrier,
    TimeSpan TimeSinceHandoff,
    DateTimeOffset DetectedAt);

/// <summary>Domain event when a carrier trace is opened for a lost shipment.</summary>
public sealed record CarrierTraceOpened(
    string Carrier,
    int TraceWindowDays,
    string TraceReferenceId,
    DateTimeOffset OpenedAt);

// --- P2 Events ---

/// <summary>Domain event when a reshipment is created for a lost, returned, or disputed shipment.</summary>
public sealed record ReshipmentCreated(
    Guid NewShipmentId,
    Guid OriginalShipmentId,
    string Reason,
    DateTimeOffset CreatedAt);

/// <summary>Domain event when a customer disputes a delivery.</summary>
public sealed record DeliveryDisputed(
    Guid CustomerId,
    int OffenseNumber,
    string Resolution,
    DateTimeOffset DisputedAt);

/// <summary>Domain event when a carrier claim is filed for a lost shipment.</summary>
public sealed record CarrierClaimFiled(
    string Carrier,
    string ClaimType,
    Guid ShipmentId,
    string? TrackingNumber,
    DateTimeOffset FiledAt);

/// <summary>Domain event when a carrier claim is resolved.</summary>
public sealed record CarrierClaimResolved(
    string Resolution,
    decimal? AmountUSD,
    DateTimeOffset ResolvedAt);

/// <summary>Domain event when fulfillment is cancelled before carrier handoff.</summary>
public sealed record FulfillmentCancelled(
    DateTimeOffset CancelledAt,
    string Reason);

/// <summary>Domain event when a rate dispute is raised with a carrier.</summary>
public sealed record RateDisputeRaised(
    string DisputeId,
    decimal OriginalBillableWeight,
    decimal ClaimedWeight,
    string Carrier,
    DateTimeOffset RaisedAt);

/// <summary>Domain event when a rate dispute is resolved.</summary>
public sealed record RateDisputeResolved(
    string DisputeId,
    string Resolution,
    decimal? AdjustedAmountUSD,
    DateTimeOffset ResolvedAt);

/// <summary>Domain event when a shipment is handed off to a third-party logistics provider.</summary>
public sealed record ThirdPartyLogisticsHandoff(
    string PartnerName,
    string ExternalOrderId,
    DateTimeOffset HandedOffAt);
