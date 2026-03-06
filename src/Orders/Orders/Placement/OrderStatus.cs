namespace Orders.Placement;

/// <summary>
/// Saga states representing the order lifecycle.
/// These states are used for internal saga coordination and are also surfaced to customers
/// via the Orders API. Internal coordination details (e.g., how many reservations are confirmed)
/// are tracked via separate saga properties rather than enum variants.
/// </summary>
public enum OrderStatus
{
    /// <summary>Order created, awaiting payment and inventory confirmation.</summary>
    Placed,

    /// <summary>Awaiting async payment confirmation (authorization received, capture pending).</summary>
    PendingPayment,

    /// <summary>Payment captured successfully.</summary>
    PaymentConfirmed,

    /// <summary>Payment declined.</summary>
    PaymentFailed,

    /// <summary>All inventory reservations confirmed across all SKUs.</summary>
    InventoryReserved,

    /// <summary>Inventory unavailable — order will be cancelled and any payment refunded.</summary>
    OutOfStock,

    /// <summary>Inventory hard-allocated; awaiting fulfillment handoff.</summary>
    InventoryCommitted,

    /// <summary>Flagged for fraud review or inventory issues.</summary>
    OnHold,

    /// <summary>Handed off to Fulfillment BC for picking, packing, and shipping.</summary>
    Fulfilling,

    /// <summary>Shipment dispatched by carrier.</summary>
    Shipped,

    /// <summary>Order delivered to customer; return window now open.</summary>
    Delivered,

    /// <summary>Order cancelled; compensation flows (inventory release, refund) triggered.</summary>
    Cancelled,

    /// <summary>Terminal state — return window expired or return fully resolved.</summary>
    Closed
}
