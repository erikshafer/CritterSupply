using System.Security.Cryptography;

namespace Fulfillment.Shipments;

/// <summary>
/// Constants for reshipment reason values used in CreateReshipment commands.
/// </summary>
public static class ReshipmentReasons
{
    public const string LostInTransit = "LostInTransit";
    public const string ReturnReceived = "ReturnReceived";
    public const string DeliveryDisputed = "DeliveryDisputed";
}

/// <summary>
/// Event-sourced aggregate representing a shipment's routing decision and carrier lifecycle.
/// Restructured for the Fulfillment BC remaster (ADR 0059).
/// Holds routing events (FulfillmentRequested, FulfillmentCenterAssigned) and all carrier
/// lifecycle events. Does NOT hold warehouse operations (those live on WorkOrder).
/// </summary>
public sealed record Shipment(
    Guid Id,
    Guid OrderId,
    Guid CustomerId,
    ShippingAddress ShippingAddress,
    IReadOnlyList<FulfillmentLineItem> LineItems,
    string ShippingMethod,
    ShipmentStatus Status,
    string? AssignedFulfillmentCenter,
    string? TrackingNumber,
    string? Carrier,
    int DeliveryAttemptCount,
    DateTimeOffset RequestedAt,
    DateTimeOffset? FulfillmentCenterAssignedAt,
    DateTimeOffset? LabelGeneratedAt,
    DateTimeOffset? HandedToCarrierAt,
    DateTimeOffset? DeliveredAt,
    string? LastScanLocation,
    DateTimeOffset? EstimatedDelivery)
{
    /// <summary>
    /// Whether the shipment is in a terminal state.
    /// </summary>
    public bool IsTerminal => Status is
        ShipmentStatus.Delivered or ShipmentStatus.Cancelled or
        ShipmentStatus.LostReplacementShipped or ShipmentStatus.ReturnedReshippable or
        ShipmentStatus.ReturnedReplacementShipped or ShipmentStatus.FulfillmentCancelled;

    /// <summary>
    /// Creates a deterministic UUID v5 from an OrderId.
    /// Ensures idempotency: the same OrderId always produces the same ShipmentId.
    /// </summary>
    public static Guid StreamId(Guid orderId)
    {
        // RFC 4122 DNS namespace UUID used as the hashing namespace
        var namespaceId = new Guid("6ba7b812-9dad-11d1-80b4-00c04fd430c8");
        var nameBytes = orderId.ToByteArray();
        var namespaceBytes = namespaceId.ToByteArray();

        using var sha1 = SHA1.Create();
        var combined = namespaceBytes.Concat(nameBytes).ToArray();
        var hash = sha1.ComputeHash(combined);

        hash[6] = (byte)((hash[6] & 0x0F) | 0x50); // Version 5
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80); // Variant

        return new Guid(hash.Take(16).ToArray());
    }

    public static Shipment Create(FulfillmentRequested @event) =>
        new(Guid.Empty,
            @event.OrderId,
            @event.CustomerId,
            @event.ShippingAddress,
            @event.LineItems,
            @event.ShippingMethod,
            ShipmentStatus.Pending,
            null,
            null,
            null,
            0,
            @event.RequestedAt,
            null, null, null, null, null, null);

    public Shipment Apply(FulfillmentCenterAssigned @event) =>
        this with
        {
            Status = ShipmentStatus.Assigned,
            AssignedFulfillmentCenter = @event.FulfillmentCenterId,
            FulfillmentCenterAssignedAt = @event.AssignedAt
        };

    public Shipment Apply(ShippingLabelGenerated @event) =>
        this with
        {
            Status = ShipmentStatus.Labeled,
            Carrier = @event.Carrier,
            LabelGeneratedAt = @event.GeneratedAt
        };

    public Shipment Apply(TrackingNumberAssigned @event) =>
        this with
        {
            TrackingNumber = @event.TrackingNumber,
            Carrier = @event.Carrier
        };

    public Shipment Apply(ShipmentManifested _) =>
        this with { Status = ShipmentStatus.Staged };

    public Shipment Apply(PackageStagedForPickup _) => this;

    public Shipment Apply(CarrierPickupConfirmed _) => this;

    public Shipment Apply(ShipmentHandedToCarrier @event) =>
        this with
        {
            Status = ShipmentStatus.HandedToCarrier,
            HandedToCarrierAt = @event.HandedAt
        };

    public Shipment Apply(ShipmentInTransit @event) =>
        this with
        {
            // ShipmentInTransit resolves GhostShipmentInvestigation if a scan arrives
            Status = ShipmentStatus.InTransit,
            LastScanLocation = @event.ScanLocation
        };

    public Shipment Apply(OutForDelivery @event) =>
        this with
        {
            Status = ShipmentStatus.OutForDelivery,
            EstimatedDelivery = @event.EstimatedDelivery
        };

    public Shipment Apply(ShipmentDelivered @event) =>
        this with
        {
            Status = ShipmentStatus.Delivered,
            DeliveredAt = @event.DeliveredAt
        };

    public Shipment Apply(DeliveryAttemptFailed @event) =>
        this with
        {
            Status = ShipmentStatus.DeliveryAttemptFailed,
            DeliveryAttemptCount = @event.AttemptNumber
        };

    public Shipment Apply(ReturnToSenderInitiated _) =>
        this with { Status = ShipmentStatus.ReturningToSender };

    public Shipment Apply(ReturnReceivedAtWarehouse _) =>
        this with { Status = ShipmentStatus.ReturnReceived };

    public Shipment Apply(ShipmentRerouted @event) =>
        this with
        {
            Status = ShipmentStatus.Assigned,
            AssignedFulfillmentCenter = @event.NewFulfillmentCenter
        };

    public Shipment Apply(BackorderCreated _) =>
        this with { Status = ShipmentStatus.Backordered };

    public Shipment Apply(ShippingLabelGenerationFailed _) =>
        this with { Status = ShipmentStatus.LabelGenerationFailed };

    public Shipment Apply(CarrierPickupMissed _) => this;

    public Shipment Apply(CarrierRelationsEscalated _) => this;

    public Shipment Apply(AlternateCarrierArranged @event) =>
        this with { Carrier = @event.NewCarrier };

    public Shipment Apply(ShippingLabelVoided _) => this;

    public Shipment Apply(GhostShipmentDetected _) =>
        this with { Status = ShipmentStatus.GhostShipmentInvestigation };

    public Shipment Apply(ShipmentLostInTransit _) =>
        this with { Status = ShipmentStatus.LostInTransit };

    public Shipment Apply(CarrierTraceOpened _) => this;

    public Shipment Apply(ReshipmentCreated @event) =>
        this with
        {
            // Terminal state depends on the reason for reshipment
            Status = @event.Reason switch
            {
                ReshipmentReasons.LostInTransit => ShipmentStatus.LostReplacementShipped,
                ReshipmentReasons.ReturnReceived => ShipmentStatus.ReturnedReplacementShipped,
                _ => ShipmentStatus.LostReplacementShipped // Default for dispute-based reshipments
            }
        };

    public Shipment Apply(DeliveryDisputed _) =>
        this with { Status = ShipmentStatus.DeliveryDisputed };

    public Shipment Apply(CarrierClaimFiled _) => this;

    public Shipment Apply(CarrierClaimResolved _) => this;

    public Shipment Apply(FulfillmentCancelled _) =>
        this with { Status = ShipmentStatus.FulfillmentCancelled };

    public Shipment Apply(RateDisputeRaised _) => this;

    public Shipment Apply(RateDisputeResolved _) => this;

    public Shipment Apply(ThirdPartyLogisticsHandoff _) =>
        this with { Status = ShipmentStatus.HandedToThirdParty };
}
