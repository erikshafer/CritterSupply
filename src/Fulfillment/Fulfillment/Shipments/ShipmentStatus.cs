namespace Fulfillment.Shipments;

/// <summary>
/// Represents the lifecycle status of a shipment.
/// Expanded for the Fulfillment BC remaster (ADR 0059).
/// </summary>
public enum ShipmentStatus
{
    /// <summary>Fulfillment request received, awaiting FC assignment.</summary>
    Pending,

    /// <summary>Routed to a fulfillment center, work order created.</summary>
    Assigned,

    /// <summary>Shipping label generated, tracking number assigned.</summary>
    Labeled,

    /// <summary>Package manifested and staged for carrier pickup.</summary>
    Staged,

    /// <summary>Physical custody transferred to carrier.</summary>
    HandedToCarrier,

    /// <summary>First carrier facility scan received — package in transit.</summary>
    InTransit,

    /// <summary>Carrier last-mile out-for-delivery scan.</summary>
    OutForDelivery,

    /// <summary>Carrier confirmed delivery (terminal — happy path).</summary>
    Delivered,

    /// <summary>One or more delivery attempts failed, carrier will retry.</summary>
    DeliveryAttemptFailed,

    /// <summary>Carrier returning package after exhausting delivery attempts.</summary>
    ReturningToSender,

    /// <summary>Returned package received back at FC.</summary>
    ReturnReceived,

    /// <summary>Shipment cancelled before dispatch.</summary>
    Cancelled,

    /// <summary>Original shipment lost — replacement shipped (terminal).</summary>
    LostReplacementShipped,

    /// <summary>Package returned and is eligible for reshipment (terminal).</summary>
    ReturnedReshippable
}
